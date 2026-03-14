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
            switch (cmd.payload_case()) {
                case GatewayCommand::kPolicy:
                    spdlog::info("[GrpcClient] Policy update received, command_id={}",
                                 cmd.command_id());
                    if (on_policy_) on_policy_(cmd.policy());
                    // 回复 AckFrame 告知网关策略已收到
                    {
                        AgentReport ack;
                        ack.mutable_ack()->set_command_id(cmd.command_id());
                        stream->Write(ack);
                    }
                    break;

                case GatewayCommand::kConfigReload:
                    // 通知主线程重新加载配置（当前版本记录日志，下次心跳时生效）
                    spdlog::info("[GrpcClient] Config reload requested, command_id={}",
                                 cmd.command_id());
                    break;

                case GatewayCommand::kShutdown:
                    spdlog::warn("[GrpcClient] Shutdown command received, stopping agent...");
                    running_ = false;
                    connected_ = false;
                    break;

                default:
                    spdlog::debug("[GrpcClient] Unknown command type: {}", cmd.payload_case());
                    break;
            }
        }
    });

    // 发送线程：从 RingBuffer 取数据，根据 topic 反序列化并填充 AgentReport
    while (running_ && connected_) {
        RingBuffer::Entry entry;
        if (!buffer_.pop(entry, 200)) continue;

        AgentReport report;
        report.set_sequence(entry.sequence);

        bool parsed = false;
        if (entry.topic == "logs") {
            parsed = report.mutable_log_entry()->ParseFromString(entry.serialized);
        } else if (entry.topic == "usb_event") {
            parsed = report.mutable_usb_event()->ParseFromString(entry.serialized);
        } else if (entry.topic == "heartbeat") {
            parsed = report.mutable_heartbeat()->ParseFromString(entry.serialized);
        } else {
            spdlog::warn("[GrpcClient] Unknown topic '{}', dropping entry seq={}",
                         entry.topic, entry.sequence);
            buffer_.ack(entry.sequence);
            continue;
        }

        if (!parsed) {
            spdlog::error("[GrpcClient] ParseFromString failed for topic '{}' seq={}",
                          entry.topic, entry.sequence);
            buffer_.ack(entry.sequence);
            continue;
        }

        if (!stream->Write(report)) {
            spdlog::warn("[GrpcClient] Write failed, connection lost (seq={})",
                         entry.sequence);
            connected_ = false;
            break;
        }
        buffer_.ack(entry.sequence);
    }

    stream->WritesDone();
    auto status = stream->Finish();
    if (!status.ok()) {
        spdlog::warn("[GrpcClient] Stream closed: {}", status.error_message());
    }

    if (recv_thread.joinable()) recv_thread.join();
}

} // namespace st
