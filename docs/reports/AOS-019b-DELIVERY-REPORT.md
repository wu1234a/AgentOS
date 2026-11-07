# AOS-019b 交付报告：Telegram 主动推送与可靠轮询

**状态：Completed**
**完成时间：2026-11-07**
**任务文件：** `tasks/AOS-019b-outbox.md`

## 1. 能力

- 审批创建后入队 `TelegramNotifications`，Worker 主动推送待审批卡片。
- 通知失败重试（指数退避，最多 `MaxSendAttempts=5`），成功记录 Sent。
- `TelegramPollerState` 持久化 offset；`TelegramProcessedUpdates` 按 update_id 去重。
- 迁移 `AddTelegramOutbox`；配置 `NotificationChatId`。

## 2. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **311 通过 / 5 跳过 / 0 失败** |
| Outbox 用例 | ✅ 审批创建生成一条通知 |
| CI | 见对应 run |

### 2.1 独立代码审查\n\nCodex CLI 审查发现 2 个 P1（Telegram offset/update 主键 identity、启动 offset 读取停 Host）与 1 个 P2（审批/通知非同一事务）。P1 已修复；P2 记录为已知限制，详见 `docs/reports/AOS-019b-CODE-REVIEW.md`。\n\n## 3. 已知限制

1. 未实现 Telegram 拒绝理由多轮采集；拒绝理由固定。
2. 未实现多实例 Outbox 租约/行锁；单 Worker 假设。
3. 429 `retry_after` 未精确处理，按普通失败退避。
4. 发送端可能重复（Telegram 无幂等键）；数据库入队/入站去重保证。
5. 无只读状态 API/前端。
6. 真实 Telegram 未在 CI 调用。

## 4. 变更文件

`TelegramModels.cs`、配置、迁移、`TelegramContracts`、`ApprovalService`、`TelegramOutbox`、
`TelegramPollingService`、Program/appsettings、`SqlTelegramOutboxTests.cs`、文档。
