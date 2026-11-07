# AOS-024b：质量门禁重评与分支保护核验

状态：Completed
优先级：P1
依赖：AOS-024
来源：Kimi Planner 草案 `01a1b6fc-4a78-71b8-97a8-00b25a2d4aa6`（已批准）

## 0. 修订

Kimi 草案含分页、审计、重试、四类核验结果。本阶段先交付：

1. 按 head_sha 查询 Actions runs 并重评必需 workflow，重新发布 status。
2. 分支保护只读核验（classic protection），返回 required/not_required/not_protected/unverified。
3. 不做自动改保护、审计 UI、Rulesets 完整核验。

## 1. 范围

- `IGitHubCommitStatusClient` 扩展：
  - `GetWorkflowRunsAsync`：`/actions/runs?head_sha` 分页（最多 10 页）。
  - `GetBranchProtectionAsync`：读取 `required_status_checks.contexts/checks`，精确匹配 `agentos/quality-gate`。
- `QualityGateService.ReevaluateAsync`：取必需 workflow 最新 run（Id/attempt），逐项 upsert/评估/发布。
- API：
  - `POST /api/v1/quality-gates/reevaluate` `{repository, headSha}`；
  - `GET /api/v1/quality-gates/branch-protection?repository=&branch=`。
- 仓库必须在 `GitHub:QualityGates` 配置中；SHA 必须 40 位 hex。

## 2. 不在范围

- 自动修改分支保护/Rulesets、merge/approve、workflow rerun。
- 前端、审计 UI、完整 Rulesets 核验、批量重评。

## 3. 安全

- 复用开发 Key 认证；仓库白名单。
- Token 仅环境变量；只读 Actions/保护，写仅 commit status。
- 403/404/歧义返回 unverified，不猜测“未要求”。

## 4. 验收

- [x] 重评按 head_sha 取最新 run 并发布。
- [x] 分支保护精确匹配 context，返回四类结果。
- [x] 后端全量测试、CI 通过。
