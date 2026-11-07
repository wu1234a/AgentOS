# AOS-020：Telegram 审批卡片交互完善

状态：Completed
优先级：P1
依赖：AOS-018、AOS-019、AOS-019b
来源：Kimi Planner 草案 `01a1b639-5064-7a02-a12f-d194ee2f1228`（已批准）

## 0. 修订

Kimi 草案含 429 retry_after、Outbox 编辑任务、会话多状态。本阶段先交付：

1. 拒绝理由会话（5 分钟）+ 理由持久化到审批。
2. Telegram 返回的 `message_id` 写入 Outbox。
3. 决策后编辑原消息；拒绝显示理由。
4. 429 精确处理、多实例租约、Outbox 编辑重试后续补。

## 1. 范围

- `TelegramRejectSessions`：ApprovalId 唯一、ChatId/UserId、MessageId、ExpiresAt。
- 点击拒绝：创建会话、提示输入理由；5 分钟内同一 chat/user 的文本作为理由。
- 理由为空重新提示；超长截断 1000；成功后 ApprovalService 拒绝并编辑卡片。
- `ITelegramClient.SendMessageAsync` 返回 message_id；Outbox 保存。
- 迁移 `AddTelegramRejectSessions`。

## 2. 不在范围

- 429 `retry_after` 精确调度、Outbox 编辑任务重试。
- 多实例租约、历史 message_id 回填。
- 拒绝理由的 Web 展示、通知、多 Bot。

## 3. 安全

- 会话绑定审批/chat/user；他人消息不能作为理由。
- 理由长度限制；纯文本编辑，无 HTML/Markdown。
- 决策仍由 ApprovalService 原子状态保护。

## 4. 验收

- [x] 拒绝需输入理由，空理由不决策。
- [x] 理由写入审批决策并编辑卡片。
- [x] Outbox 保存 message_id。
- [x] 后端全量测试、CI 通过。
