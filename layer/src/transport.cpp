#include "transport.hpp"

#include <arpa/inet.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <unistd.h>

#include <atomic>
#include <chrono>
#include <cerrno>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <memory>
#include <mutex>
#include <span>
#include <stop_token>
#include <thread>
#include <vector>

namespace {

namespace protocol = maquestlink::protocol;

struct Connection {
  explicit Connection(int socket_fd) : fd(socket_fd) {}
  ~Connection() { close(); }

  void close() {
    const int socket_fd = fd.exchange(-1);
    if (socket_fd >= 0) {
      (void)::shutdown(socket_fd, SHUT_RDWR);
      (void)::close(socket_fd);
    }
  }

  std::atomic<int> fd{-1};
  std::mutex send_mutex;
};

class TransportServer {
 public:
  static TransportServer& instance() {
    static TransportServer server;
    return server;
  }

  ~TransportServer() { stop_all(); }

  void start() {
    std::scoped_lock lock(lifecycle_mutex_);
    ++users_;
    if (users_ == 1) {
      accept_thread_ = std::jthread([this](std::stop_token stop) { accept_loop(stop); });
    }
  }

  void stop() {
    std::unique_lock lock(lifecycle_mutex_);
    if (users_ == 0 || --users_ != 0) {
      return;
    }
    stop_locked(lock);
  }

  [[nodiscard]] bool connected() const {
    const auto connection = current_connection();
    return connection != nullptr && connection->fd.load() >= 0;
  }

  void send_message(const protocol::Message& message) {
    const auto bytes = protocol::serialize(message);
    const auto connection = current_connection();
    if (connection == nullptr) {
      return;
    }
    std::scoped_lock lock(connection->send_mutex);
    std::size_t offset{};
    while (offset < bytes.size()) {
      const int socket_fd = connection->fd.load();
      if (socket_fd < 0) {
        return;
      }
      const ssize_t sent = ::send(socket_fd, bytes.data() + offset, bytes.size() - offset, 0);
      if (sent <= 0) {
        connection->close();
        return;
      }
      offset += static_cast<std::size_t>(sent);
    }
  }

  [[nodiscard]] std::optional<protocol::PoseInput> latest_pose_input() const {
    std::scoped_lock lock(pose_mutex_);
    if (latest_pose_input_.has_value() &&
        std::chrono::steady_clock::now() - latest_pose_received_at_ >
            std::chrono::milliseconds(500)) {
      return std::nullopt;
    }
    return latest_pose_input_;
  }

 private:
  [[nodiscard]] std::shared_ptr<Connection> current_connection() const {
    std::scoped_lock lock(connection_mutex_);
    return connection_;
  }

  static bool receive_exact(const std::shared_ptr<Connection>& connection,
                            std::span<std::byte> output) {
    std::size_t offset{};
    while (offset < output.size()) {
      const int socket_fd = connection->fd.load();
      if (socket_fd < 0) {
        return false;
      }
      const ssize_t received = ::recv(socket_fd, output.data() + offset,
                                      output.size() - offset, 0);
      if (received <= 0) {
        return false;
      }
      offset += static_cast<std::size_t>(received);
    }
    return true;
  }

  void receive_loop(std::stop_token stop, const std::shared_ptr<Connection>& connection) {
    try {
      while (!stop.stop_requested() && connection->fd.load() >= 0) {
        std::vector<std::byte> bytes(protocol::kHeaderSize);
        if (!receive_exact(connection, bytes)) {
          break;
        }
        const protocol::MessageHeader header = protocol::parse_header(bytes);
        bytes.resize(protocol::kHeaderSize + header.payload_size);
        if (!receive_exact(connection,
                           std::span<std::byte>(bytes).subspan(protocol::kHeaderSize))) {
          break;
        }
        protocol::Message message = protocol::deserialize(bytes);
        if (auto* pose = std::get_if<protocol::PoseInput>(&message.payload); pose != nullptr) {
          std::scoped_lock lock(pose_mutex_);
          latest_pose_input_ = *pose;
          latest_pose_received_at_ = std::chrono::steady_clock::now();
        } else if (auto* control = std::get_if<protocol::ControlMessage>(&message.payload);
                   control != nullptr && control->kind == protocol::ControlKind::Ping) {
          std::vector<std::byte> echoed_timestamp(sizeof(control->timestamp_ns));
          for (std::size_t index = 0; index < echoed_timestamp.size(); ++index) {
            echoed_timestamp[index] =
                static_cast<std::byte>((control->timestamp_ns >> (index * 8U)) & 0xffU);
          }
          const auto host_now_ns = static_cast<std::uint64_t>(
              std::chrono::duration_cast<std::chrono::nanoseconds>(
                  std::chrono::steady_clock::now().time_since_epoch())
                  .count());
          send_message(protocol::Message{
              .sequence = message.sequence,
              .payload = protocol::ControlMessage{
                  .kind = protocol::ControlKind::Pong,
                  .timestamp_ns = host_now_ns,
                  .data = std::move(echoed_timestamp),
              },
          });
        }
      }
    } catch (const protocol::ProtocolError& error) {
      std::cerr << "[MaQuestLink transport] protocol error: " << error.what() << '\n';
    }
    connection->close();
  }

  void accept_loop(std::stop_token stop) {
    const int server = ::socket(AF_INET, SOCK_STREAM, 0);
    if (server < 0) {
      return;
    }
    server_fd_.store(server);
    int reuse = 1;
    (void)::setsockopt(server, SOL_SOCKET, SO_REUSEADDR, &reuse, sizeof(reuse));
    sockaddr_in address{};
    address.sin_family = AF_INET;
    address.sin_addr.s_addr = htonl(INADDR_ANY);
    const char* port_value = std::getenv("MAQUESTLINK_PORT");
    const int port = port_value == nullptr ? 42424 : std::atoi(port_value);
    address.sin_port = htons(static_cast<std::uint16_t>(port));
    if (port <= 0 || port > 65535 ||
        ::bind(server, reinterpret_cast<sockaddr*>(&address), sizeof(address)) != 0 ||
        ::listen(server, 1) != 0) {
      std::cerr << "[MaQuestLink transport] failed to listen on TCP port " << port << '\n';
      close_server(server);
      return;
    }
    while (!stop.stop_requested()) {
      const int accepted = ::accept(server, nullptr, nullptr);
      if (accepted < 0) {
        if (!stop.stop_requested() && errno != EBADF && errno != EINVAL) {
          std::cerr << "[MaQuestLink transport] accept failed: " << std::strerror(errno) << '\n';
        }
        break;
      }
      int no_sigpipe = 1;
      (void)::setsockopt(accepted, SOL_SOCKET, SO_NOSIGPIPE, &no_sigpipe, sizeof(no_sigpipe));
      auto connection = std::make_shared<Connection>(accepted);
      {
        std::scoped_lock lock(connection_mutex_, pose_mutex_);
        connection_ = connection;
        latest_pose_input_.reset();
      }
      receive_loop(stop, connection);
      {
        std::scoped_lock lock(connection_mutex_, pose_mutex_);
        if (connection_ == connection) {
          connection_.reset();
          latest_pose_input_.reset();
        }
      }
    }
    close_server(server);
  }

  void close_server(int expected_fd) {
    int current = expected_fd;
    if (server_fd_.compare_exchange_strong(current, -1)) {
      (void)::shutdown(expected_fd, SHUT_RDWR);
      (void)::close(expected_fd);
    }
  }

  void stop_all() {
    std::unique_lock lock(lifecycle_mutex_);
    users_ = 0;
    stop_locked(lock);
  }

  void stop_locked(std::unique_lock<std::mutex>& lock) {
    accept_thread_.request_stop();
    const int server = server_fd_.exchange(-1);
    if (server >= 0) {
      (void)::shutdown(server, SHUT_RDWR);
      (void)::close(server);
    }
    if (const auto connection = current_connection(); connection != nullptr) {
      connection->close();
    }
    lock.unlock();
    if (accept_thread_.joinable()) {
      accept_thread_.join();
    }
    lock.lock();
    {
      std::scoped_lock state_lock(connection_mutex_, pose_mutex_);
      connection_.reset();
      latest_pose_input_.reset();
    }
  }

  mutable std::mutex lifecycle_mutex_;
  std::size_t users_{};
  std::jthread accept_thread_;
  std::atomic<int> server_fd_{-1};
  mutable std::mutex connection_mutex_;
  std::shared_ptr<Connection> connection_;
  mutable std::mutex pose_mutex_;
  std::optional<protocol::PoseInput> latest_pose_input_;
  std::chrono::steady_clock::time_point latest_pose_received_at_{};
};

}  // namespace

void transport_start() { TransportServer::instance().start(); }
void transport_stop() { TransportServer::instance().stop(); }
bool transport_connected() { return TransportServer::instance().connected(); }
void transport_send(const maquestlink::protocol::Message& message) {
  TransportServer::instance().send_message(message);
}
std::optional<maquestlink::protocol::PoseInput> transport_latest_pose_input() {
  return TransportServer::instance().latest_pose_input();
}
