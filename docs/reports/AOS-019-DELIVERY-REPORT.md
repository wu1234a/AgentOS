# AOS-019 交付报告：Telegram Bot 审批交互

**状态：Completed**
**完成时间：2026-11-07**
**任务文件：** `tasks/AOS-019-telegram.md`

## 1. 能力

- Telegram 长轮询 Worker（无公网回调）。
- `/pending` 命令：白名单 chat 返回最多 10 条待审批，每条带“批准/拒绝”内联按钮。
- 回调 `approve:<id>` / `reject:<id>`：调用 ApprovalService，answerCallback，成功后编辑消息。
- 配置：`Telegram:Enabled/BotToken/AllowedChatIds/AllowedUserIds/PollTimeoutSeconds`。
- Token 从 `Telegram__BotToken` 环境变量注入；appsettings 默认禁用。

## 2. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **310 通过 / 5 跳过 / 0 失败** |
| Telegram 用例 | ✅ 3（白名单 `/pending`、越权无数据、批准回调） |
| CI | 见对应 run |

### 2.1 独立代码审查\n\nCodex CLI 审查发现 2 个 P1（Token 进入 HTTP 日志、永久失败 update 卡死轮询），全部修复，详见 `docs/reports/AOS-019-CODE-REVIEW.md`。\n\n## 3. 已知限制

1. 没有新审批主动推送/Outbox；需要用户执行 `/pending` 拉取。
2. 没有 update_id/callback 持久化去重/offset 恢复；重启后可能重复处理，但审批决策由原子状态保证只成功一次。
3. 拒绝理由固定为 “Rejected via Telegram”，不支持多轮输入。
4. 未实现 Webhook、多 Bot/多租户、群管理。
5. 真实 Telegram Bot 未在 CI 调用；测试使用 Fake client。

## 4. 配置示例

```json
"Telegram": {
  "Enabled": true,
  "BotToken": null,
  "AllowedChatIds": [123456789],
  "AllowedUserIds": [987654321],
  "PollTimeoutSeconds": 30
}
```

环境变量：`Telegram__BotToken=<bot token>`。

## 5. 变更文件

`TelegramOptions.cs`、`TelegramClient.cs`、`TelegramUpdateProcessor.cs`、
`TelegramPollingService.cs`、Program/appsettings、`TelegramUpdateProcessorTests.cs`、文档。
