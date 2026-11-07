# AOS-019：Telegram Bot 审批交互

状态：Completed
优先级：P1
依赖：AOS-018
来源：Kimi Planner 草案 `01a1b5cd-d7ab-72b0-91dc-523528d455e0`（已批准）

## 0. 修订

Kimi 草案含通知 Outbox、持久化 update 去重、`answerCallback`/编辑消息、重启恢复。本阶段先交付：

1. 配置 + 长轮询 Worker + `/pending` 命令 + 批准/拒绝回调。
2. 不做主动新审批推送（Outbox）、不做 update 持久化去重；决策由 ApprovalService 原子状态保证只成功一次。
3. 白名单 chat/user 双校验；Token 仅环境变量。
4. 不做 Webhook、多 Bot、多租户。

## 1. 范围

- `TelegramOptions`：Enabled、BotToken、AllowedChatIds、AllowedUserIds、PollTimeoutSeconds。
- `ITelegramClient`/`HttpTelegramClient`：getUpdates、sendMessage、answerCallbackQuery、editMessageText。
- `TelegramPollingService` BackgroundService：长轮询、offset 内存推进、异常退避。
- `TelegramUpdateProcessor`：
  - 仅白名单 chat+user；
  - `/pending` 返回最多 10 条待审批并附批准/拒绝按钮；
  - 回调 `approve:<id>` / `reject:<id>` 调用 ApprovalService，answerCallback 并编辑消息。
- Token 通过 `Telegram__BotToken`；appsettings 默认 Enabled=false。

## 2. 不在范围

- 主动推送新审批、通知 Outbox/重试。
- update_id/callback 持久化去重、重启恢复 offset。
- Webhook、多 Bot/多租户、群管理、拒绝理由输入。
- 自动执行/合并。

## 3. 安全

- chat 与 user 双重白名单；空白名单拒绝启动。
- 回调动作枚举 + GUID 校验；不信任 callback_data 伪造。
- 不复用开发 Key；Token 仅环境变量，日志不含 URL/Token。

## 4. 验收

- [x] `/pending` 只对白名单发送待审批。
- [x] 批准/拒绝回调走 ApprovalService 并返回结果。
- [x] 非白名单无数据泄漏。
- [x] 配置缺失时 Worker 不启动。
- [x] 后端全量测试、CI 通过。
