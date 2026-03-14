#include "GrpcClient.h"
#include <spdlog/spdlog.h>
#include <chrono>
#include <fstream>
#include <sstream>

namespace st {

GrpcClient::GrpcClient(const GatewayConfig& cfg, RingBuffer& buffer, PolicyCallback on_policy)
    : cfg_(cfg), buffer_(buffer), on_policy_(std::move(on_policy)) {}

GrpcClient::~GrpcClient() { stop(); }

void GrpcClient::start() {
    running_ = true;
    worker_ = std::thread([this] { run_loop(); });
}

void GrpcClient::stop() {
    running_ = false;
    if (worker_.joinable()) worker_.join();
}

grpc::SslCredentials GrpcClient::build_credentials() const {
    auto read_file = [](const std::string& path) {
        std::ifstream f(path);
        return std::string(std::istreambuf_iterator<char>(f), {});
    };

    grpc::SslCredentialsOptions opts;
    opts.pem_root_certs    = read_file(cfg_.ca_cert);
    opts.pem_cert_chain    = read_file(cfg_.cert_file);
    opts.pem_private_key   = read_file(cfg_.key_file);
    return grpc::SslCredentials(opts);
}

void GrpcClient::run_loop() {
    int32_t delay_ms = cfg_.reconnect_initial_ms;

    while (running_) {
        spdlog::info("[GrpcClient] Connecting to {}", cfg_.address);
        try {
            connect_and_stream();
        } catch (const std::exception& e) {
            spdlog::error("[GrpcClient] Stream error: {}", e.what());
        }

        connected_ = false;
        if (!running_) break;

        spdlog::info("[GrpcClient] Reconnecting in {}ms", delay_ms);
        std::this_thread::sleep_for(std::chrono::milliseconds(delay_ms));
        // 指数退避，上限 reconnect_max_ms
        delay_ms = std::min(delay_ms * 2, cfg_.reconnect_max_ms);
    }
}

void GrpcClient::connect_and_stream() {
    auto creds = build_credentials();
    channel_ = grpc::CreateChannel(cfg_.address, creds);
    stub_ = terminal::v1::TerminalGatewayService::NewStub(channel_);

    grpc::ClientContext ctx;
    auto stream = stub_->Connect(&ctx);
    connected_ = true;
    spdlog::info("[GrpcClient] Stream established");

    // 接收线程：处理服务端下发的命令
    std::thread recv_thread([&] {
        GatewayCommand cmd;
        while (stream->Read(&cmd)) {
            if (cmd.has_policy()) {
                spdlog::info("[GrpcClient] Received policy update, id={}", cmd.payload_case());
                if (on_policy_) on_policy_(cmd.policy());
            }
            // TODO: 处理 config_reload / shutdown 命令
        }
    });

    // 发送线程：从 RingBuffer 取数据发送
    while (running_ && connected_) {
        RingBuffer::Entry entry;
        if (buffer_.pop(entry, 200)) {
            AgentReport report;
            // TODO: 根据 entry.topic 反序列化并填充 report
            // report.mutable_log_entry()->ParseFromString(entry.serialized);
            if (!stream->Write(report)) {
                spdlog::warn("[GrpcClient] Write failed, connection lost");
                connected_ = false;
                break;
            }
            buffer_.ack(entry.sequence);
        }
    }

    stream->WritesDone();
    auto status = stream->Finish();
    if (!status.ok()) {
        spdlog::warn("[GrpcClient] Stream closed: {}", status.error_message());
    }

    if (recv_thread.joinable()) recv_thread.join();
}

} // namespace st
