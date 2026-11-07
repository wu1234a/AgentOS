# AOS-022 交付报告：GitHub Webhook 与 PR 事件

**状态：Completed**
**完成时间：2026-11-08**
**任务文件：** `tasks/AOS-022-github-webhook.md`

## 1. 能力

- `POST /api/v1/webhooks/github`：
  - HMAC-SHA256 验签（`X-Hub-Signature-256`）；
  - `X-GitHub-Delivery`/`X-GitHub-Event` 必需；
  - 1MB body 限制、application/json；
  - 仓库白名单 + 事件类型过滤；
  - PR/push/workflow_run 规范化；
  - 写入 Event Hub（`DedupeKey=github:<delivery>`），复用事件去重与规则引擎。
- 响应语义：202/200/401/400/413/415/503。

## 2. 配置

```json
"GitHub": {
  "Enabled": true,
  "WebhookSecret": null,
  "AllowedRepositories": ["owner/repo"],
  "AllowedEvents": ["pull_request", "push", "workflow_run"]
}
```
环境变量：`GitHub__WebhookSecret`。

## 3. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **313 通过 / 5 跳过 / 0 失败** |
| 验签用例 | ✅ 有效/篡改/SHA-1 拒绝 |
| CI | 见对应 run |

## 4. 已知限制

1. 未单独测试完整 HTTP 端点（仅验签单元测试）。
2. 无独立 delivery 处理状态/失败重试表；依赖 Event Hub 的 DedupeKey。
3. 未配置仓库到 AgentOS 项目的映射，事件 Project 为空，规则可能需按 source/type 匹配。
4. 未实现 PR 评论/Review/Check/合并。
5. 真实 GitHub Webhook 未在 CI 调用。

## 5. 变更文件

`GitHubOptions.cs`、`GitHubWebhookEndpoints.cs`、Program/appsettings、
`GitHubWebhookTests.cs`、文档。
