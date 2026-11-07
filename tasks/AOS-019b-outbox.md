# AOS-019b：Telegram 主动推送与可靠轮询

状态：Completed
优先级：P1
依赖：AOS-018、AOS-019
来源：Kimi Planner 草案 `01a1b62d-e211-71d8-b61a-ed5c0875005d`（已批准）

## 0. 修订

Kimi 草案含拒绝理由多轮、行锁租约、429 retry_after、只读状态 API。本阶段先交付：

1. `TelegramNotifications` Outbox + 审批创建入队 + Worker 发送/重试/最大次数。
2. `TelegramPollerState` 持久化 offset、`TelegramProcessedUpdates` 去重。
3. 不做拒绝理由采集、多实例租约、429 精确处理、状态 API。
4. 仍单 Bot/长轮询。

## 1. 范围

- `TelegramNotifications`：ApprovalId 唯一、ChatId、Status、AttemptCount、NextRetry、LastError、MessageId。
- `TelegramPollerState`：NextOffset；`TelegramProcessedUpdates`：UpdateId 唯一。
- 配置 `Telegram:NotificationChatId`、`MaxSendAttempts`。
- 审批创建（启用且有通知 chat）同事务写入通知；失败不影响审批。
- Worker：每轮发送 Pending 通知；失败按指数退避上限 5 次；发送成功标记 Sent。
- Worker：启动读取 offset；update 已处理则跳过；处理后记录并保存 offset。
- 迁移 `AddTelegramOutbox`。

## 2. 不在范围

- 拒绝理由多轮、消息编辑/按钮移除、429 retry_after 精确处理。
- 多实例租约、状态 API、Webhook、多 Bot。
- 发送端 exactly-once（Telegram 无幂等键）。

## 3. 安全

- Token 仅环境变量；outbox 不存 Token/完整消息。
- 仅通知配置 chat；回调白名单沿用 AOS-019。
- 审批决策仍由 ApprovalService 原子状态保证。

## 4. 验收

- [x] 审批创建生成一条通知；禁用/无 chat 不入队。
- [x] Worker 发送并标记；失败重试/上限。
- [x] offset 持久化、update 去重。
- [x] 后端全量测试、CI 通过。
