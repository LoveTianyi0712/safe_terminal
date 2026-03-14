#pragma once
#include "RingBuffer.h"
#include "../config/Config.h"
#include "terminal.grpc.pb.h"
#include <grpcpp/grpcpp.h>
#include <atomic>
#include <thread>
#include <functional>

namespace st {

using terminal::v1::GatewayCommand;
using terminal::v1::AgentReport;

// 收到策略变更时的回调
using PolicyCallback = std::function<void(const terminal::v1::Policy&)>;

/**
 * gRPC 双向流客户端
 * - 自动重连（指数退避）
 * - 从 RingBuffer 消费数据并发送
 * - 接收 GatewayCommand 并触发回调
 */
class GrpcClient {
public:
    GrpcClient(const GatewayConfig& cfg, RingBuffer& buffer, PolicyCallback on_policy);
    ~GrpcClient();

    void start();
    void stop();

    bool is_connected() const { return connected_.load(); }

private:
    void run_loop();
    void connect_and_stream();
    grpc::SslCredentials build_credentials() const;

    GatewayConfig   cfg_;
    RingBuffer&     buffer_;
    PolicyCallback  on_policy_;

    std::atomic<bool>     running_{false};
    std::atomic<bool>     connected_{false};
    std::thread           worker_;

    std::shared_ptr<grpc::Channel>                                          channel_;
    std::unique_ptr<terminal::v1::TerminalGatewayService::Stub>             stub_;
};

} // namespace st
