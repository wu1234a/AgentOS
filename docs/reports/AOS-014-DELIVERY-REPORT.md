# AOS-014 交付报告：独立 Review Agent

**状态：Completed**
**完成时间：2026-11-07**
**任务文件：** `tasks/AOS-014-review-agent.md`

## 1. 能力

- Coding Run 可记录 `baseRevision`；Runner 成功后采集不可变变更集：
  `HEAD`、`git diff base..HEAD`、变更文件清单，上传 `changeset.diff` / `changeset.json`。
- `ReviewRuns` / `ReviewFindings` 持久化审查状态、结论、摘要与结构化问题。
- `POST/GET /api/v1/coding-runs/{id}/review`：手动发起/查询；已成功/进行中幂等，Failed 可重试。
- `/coding-runs` 详情展示 Review 面板：状态、结论、摘要、findings（级别/文件/行/理由/建议）。
- Review 使用 Codex CLI 只读沙箱；不修改代码、不合并、不执行校验命令。

## 2. 数据与 API

- 迁移 `AddReviewAgent`：`ReviewRuns`（CodingRunId 唯一）、`ReviewFindings`
  （ReviewRunId + SortOrder 唯一）。
- `POST /api/v1/coding-runs/{id}/review`：200 + ReviewRun；409 `coding-run-not-succeeded` /
  `changeset-missing` / `changeset-empty` / `coding-run-task-missing`。
- `GET /api/v1/coding-runs/{id}/review`：ReviewRun + findings；未发起 404。
- Coding Run 创建新增可选 `baseRevision`；校验 7–64 位十六进制。

## 3. 安全

- Review 只读沙箱，固定提示词；diff 内容不能改变审查规则。
- 路径必须为相对路径、拒绝 `..` 与绝对路径；最多 100 findings；文本截断。
- 不持久化完整 diff/模型原始输出；错误信息不含 Key。
- 审查不改变 Coding Run、任务或校验结果，不自动合并/修复。

## 4. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **292 通过 / 5 跳过 / 0 失败** |
| Review Service 用例 | ✅ 3（成功持久化/幂等、非 Succeeded 拒绝、Failed 重试） |
| Handler 用例 | ✅ 新增 baseRevision → changeset artifacts |
| 前端 lint / build | ✅ 通过 |
| CI | 见对应 run |

## 5. 已知限制

1. Review 同步执行，最长 600s；无后台队列/进度轮询。
2. 未自动触发；必须人工在 Run 详情点击。
3. 未实现多 Reviewer/多模型、历史趋势、自动修复/合并。
4. 真实 Codex 审查未在 CI 执行；Fake Agent 覆盖服务逻辑，Parser 依赖 Codex 返回严格 JSON。
5. `changeset.json` 的 files 列表为 name-status 解析后的路径；如 git 输出异常会安全失败。

## 6. 变更文件

**Domain/Infra**：`Domain/Reviews/ReviewRun.cs`、Review 配置、迁移 `AddReviewAgent`、
`Infrastructure/Reviews/ReviewRepository.cs`。

**Application/API**：`Application/Reviews/*`、`Api/Reviews/CodexReviewAgent.cs`、Review 端点、
`CodingRunContracts/Service`（baseRevision）、Program 注册。

**Runner**：`IChangeSetCapture.cs`、`CodexTaskHandler`、`ServiceRegistration`。

**前端**：`CodingRunsView.vue`、`api/reviews.js`。

**测试**：`ReviewServiceTests.cs`、`CodexTaskHandlerTests.cs`。

## 7. 下一步

AOS-017 Git Worktree 隔离，或 AOS-015 QA Agent；真实 Codex Review 冒烟需在配置 Codex 的环境人工验证。
