# AOS-014：独立 Review Agent（修订版）

状态：Completed
优先级：P1
依赖：AOS-013、AOS-013b、AOS-007、AOS-012
来源：Kimi Planner 草案 `01a1b45d-c0c8-79a2-92f6-a65b85cea305`（已批准）

## 0. 对 Kimi 草案的修订

Kimi 草案假设 AOS-013 已提供不可变 commit/diff，但实际 AOS-013 只有草案快照，没有代码变更集。
因此本任务先补齐变更集捕获，再做 Review Agent；同时改为 Codex 只读审查，Kimi 仍只负责 Planner。

1. **新增变更集捕获（本任务 Phase A）**：Creating Run 时记录 `baseRevision`；Runner 在 Codex
   成功后采集 `git diff base..HEAD`、HEAD 与变更文件清单，上传为任务 Artifact。
2. **Review 使用 Codex 只读会话**：固定提示词、只读沙箱、无写文件/Shell 工具；不复用 Kimi
   Planner Profile，也不让 Review 修改代码。
3. **UI 落在 `/coding-runs` 详情**：当前没有任务详情页承载 Coding Run，先在 Run 详情展示审查。
4. **MVP 同步审查**：POST 在请求内完成（最长 600s），不做后台队列；失败可按规则重试。
5. **不实现自动触发**：Coding Run 成功不自动审查，必须人工点击。

## 1. 目标

对已完成且捕获了不可变变更集的 Coding Run 执行只读代码审查，持久化结构化 findings，并在
Coding Run 详情展示。审查是辅助意见，不自动合并、不修改代码、不改变 Coding Run 状态。

## 2. Phase A：变更集捕获

- `CreateCodingRunRequest` 增加可选 `baseRevision`（40/64 位十六进制 commit）。
- 快照保存 `baseRevision`；没有 baseRevision 的 Run 不可发起 Review。
- Runner `CodexTaskHandler` 在 Codex 成功后：
  - 校验 workspace 是 git 仓库且 `baseRevision` 可达；
  - 采集 `HEAD`、`git diff --name-status base..HEAD`、`git diff --no-ext-diff base..HEAD`；
  - 通过现有 Artifact 上传接口保存 `changeset.json`、`changeset.diff`；
  - 采集失败 → `changeset-error`（不可重试），不把任务标记成功。
- `CodingRun` 增加 `BaseRevision`/`HeadRevision`（可选）；Review 通过任务 Artifact 名称读取。

## 3. Phase B：Review

### database

- `ReviewRuns`：Id、CodingRunId（唯一）、BaseRevision、HeadRevision、Status
  (`Queued/Running/Succeeded/Failed`)、Conclusion (`NoIssues/IssuesFound`)、Summary、
  ErrorCode/Message、AttemptCount、CreatedAt/StartedAt/CompletedAt、RowVersion。
- `ReviewFindings`：ReviewRunId、SortOrder、Severity (`Critical/Major/Minor/Info`)、
  FilePath、StartLine/EndLine、Reason、Suggestion。
- 迁移 `AddReviewAgent`；CodingRunId 唯一，findings 级联删除。

### backend

- `IReviewAgent`：输入冻结 diff、变更文件清单、Run 元数据；输出结构化 findings。
- `CodexReviewAgent`：Codex CLI 只读沙箱、固定系统提示、要求 JSON；解析失败/超时/超限 →
  Failed，不保存部分 findings。
- `ReviewService`：
  - 仅 Succeeded Coding Run 且存在 `changeset.diff` Artifact 可发起；
  - 已 Queued/Running → 返回当前；Succeeded → 返回已有；Failed → 允许重试并递增 Attempt；
  - 同步执行；findings 与 ReviewRun 一次事务保存；
  - 限制 diff ≤ 1MB、findings ≤ 100、单条文本 ≤ 2000；拒绝绝对路径/路径穿越；
  - 不记录完整 diff 或模型原始响应。
- 权限：沿用现有开发 Key；后续 RBAC 接入点保留。

### API

- `POST /api/v1/coding-runs/{id}/review`：发起/重试，返回 200/201（同步完成）。
- `GET /api/v1/coding-runs/{id}/review`：返回 ReviewRun + findings；未发起 404。
- 409：Run 未成功、缺少变更集、审查进行中重复请求、不可重试状态。

### frontend

- `/coding-runs` 详情增加 Review 面板：
  - 未审查：按钮（Run 未成功或无变更集时禁用并说明）；
  - 成功：结论、摘要、按严重级别排序的 findings（文件/行号/理由/建议）；
  - 失败：错误码与重试入口；
  - 所有文本按纯文本渲染，不使用 `v-html`；不提供修复/合并按钮。

## 4. 不在本任务范围

- 自动触发审查、自动修复、自动合并、PR 门禁。
- 多 Reviewer/多模型路由、历史趋势、跨 Run 聚合。
- 新 diff 查看器、代码编辑器、容器沙箱。
- 生产部署与敏感数据审查。

## 5. 安全

- Review 只读；不授予写文件、Git 写、Shell、部署能力。
- Diff/代码注释视为不可信数据，固定提示词，不能被内容覆盖规则。
- 路径校验、大小/数量上限、超时；日志/错误不含 Key、完整 diff。
- 审查结果不改变 Coding Run/校验结论，只是辅助意见。

## 6. 验收

- [ ] 创建 Run 可记录 baseRevision；Runner 成功后上传 changeset 两个 Artifact；失败任务不成功。
- [ ] 仅 Succeeded + 有 changeset 的 Run 可发起审查；其他情况 409。
- [ ] Fake Review Agent 生成合法 findings 并事务持久化；重复请求幂等；Failed 可重试。
- [ ] 非法 JSON、路径穿越、超限、超时均安全失败，无部分 findings。
- [ ] GET 返回结论/findings，前端详情正确展示，文本转义。
- [ ] 审查不修改工作区/Run/校验结果；无自动合并/修复。
- [ ] 后端全量测试、前端 lint/build、CI 通过。

## 7. 交付

`docs/reports/AOS-014-DELIVERY-REPORT.md`；实现后按流程 Codex 审查并修复。
