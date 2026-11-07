# AOS-022：GitHub Webhook 与 PR 事件接入

状态：Completed
优先级：P1
依赖：AOS-002 Event Hub、AOS-004 Rules、AOS-013 CodingRun
来源：Kimi Planner 草案 `01a1b6d3-bc7d-7acf-8d81-caeaf4380be3`（已批准）

## 0. 修订

Kimi 草案含独立 delivery 去重表、多事件字段、失败重试语义。本阶段先交付：

1. `POST /api/v1/webhooks/github`，HMAC-SHA256 验签。
2. 仓库白名单 + 事件类型过滤，空白名单拒绝。
3. PR/push/workflow_run 规范化为现有 Event Hub 事件；使用 `github:<delivery>` 作 DedupeKey，复用现有事件去重。
4. 不新增独立 delivery 表、不做 PR 评论/合并/Review 回写。

## 1. 范围

- 配置：`GitHub:Enabled/WebhookSecret/AllowedRepositories/AllowedEvents`；Secret 从环境变量。
- 端点：读取原始 body（≤1MB），校验 `X-Hub-Signature-256`、`X-GitHub-Delivery`、`X-GitHub-Event`。
- 过滤：仓库 full_name 白名单；事件类型白名单。
- 规范化字段：
  - PR：number/action/title/state/draft/merged/user/base/head/ref/sha；
  - push：ref/before/after/created/deleted/forced/compare/head_commit；
  - workflow_run：id/name/run_number/event/status/conclusion/head_branch/head_sha/html_url。
- 通过 `EventService.CreateAsync` 写入 Event Hub，触发现有规则/任务管线。
- 响应：202 accepted、200 duplicate/ignored、401 签名、400 头/JSON、413 超限、415 媒体类型。

## 2. 不在范围

- 独立 delivery 状态表、失败重试队列。
- PR 评论/Review/Check/合并、GitHub App/OAuth。
- 其他事件类型/GitLab。
- 前端配置页。

## 3. 安全

- 验签先于解析/过滤/发布；恒定时间比较；只接受 sha256。
- Secret 仅环境变量；日志不含 payload/签名/密钥。
- 仓库白名单默认拒绝；不信任 payload 绕过签名。
- Webhook 只写 Event Hub；规则/任务仍走既有校验。

## 4. 验收

- [x] 有效/错误/篡改/SHA-1 签名行为正确（单元测试）。
- [x] 仓库/事件过滤、空白名单拒绝。
- [x] 三类事件规范化并通过 Event Hub 去重。
- [x] 后端全量测试、CI 通过。
