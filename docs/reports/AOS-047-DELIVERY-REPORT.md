# AOS-047 Delivery Report：立项预算、版本与启动审批

- 日期：2026-11-13
- 状态：Completed
- 范围：Token 预算、版本快照、二次启动审批

## 1. 变更文件

后端：

- `backend/AgentOS.Application/Planning/PlannerContracts.cs`
- `backend/AgentOS.Infrastructure/Planning/ApiCompletionClient.cs`
- `backend/AgentOS.Domain/Planning/ProjectPlan.cs`
- `backend/AgentOS.Infrastructure/Persistence/Configurations/ProjectPlanConfiguration.cs`
- `backend/AgentOS.Infrastructure/Migrations/20261113064836_AddProjectPlanBudget.cs`（新增）
- `backend/AgentOS.Infrastructure/Migrations/20261113064947_AddProjectPlanReleaseApproval.cs`（新增）
- `backend/AgentOS.Api/ProjectPlanning/ProjectPlanModelRunner.cs`
- `backend/AgentOS.Api/ProjectPlanning/ProjectPlanService.cs`
- `backend/AgentOS.Api/ProjectPlanning/ProjectPlanWorker.cs`
- `backend/AgentOS.Api/Endpoints/ProjectPlanEndpoints.cs`

前端：

- `frontend/agentos-web/src/api/projectPlans.js`
- `frontend/agentos-web/src/views/ProjectPlansView.vue`

文档：

- `tasks/AOS-047-plan-budget-approval.md`（新增）
- `docs/reports/AOS-047-DELIVERY-REPORT.md`（本文件）

## 2. Token 预算

- 新建计划可设置 `TokenBudget`，默认 60,000，范围 10,000–500,000。
- `ApiCompletionClient` 解析 OpenAI `usage.prompt_tokens` / `usage.completion_tokens`。
- Planner 每次调用累加 `TokensUsed`、`ModelCalls`。
- 达到预算后不再发起下一次模型调用，计划失败并记录原因。
- 前端显示 `TokensUsed / TokenBudget` 和 `ModelCalls / 7`。

说明：provider 未返回 usage 时按响应字符数/4 估算，不是账单级精确值。

## 3. 上一版方案

- 修订时把当前 `FinalPlanJson` 写入 `PreviousFinalPlanJson`，记录 `PreviousRevision`。
- 前端新增“上一版”标签页，显示上一版推荐方案和任务。
- 当前只保留上一版，不是完整版本历史。

## 4. 二次启动审批

- 新建计划可勾选“启动需二次审批”，默认开启。
- `POST /project-plans/{id}/approve-release` 记录 `ReleaseApprovedAtUtc`。
- `release` 在 `requiresReleaseApproval=true` 且未审批时返回 HTTP 409。
- 修订后清空启动审批，必须重新批准。
- 前端 Approved 状态先显示“批准启动”，通过后才显示“启动执行”。

## 5. 验证

```text
GET plan → requiresReleaseApproval=true, releaseApprovedAtUtc=null
POST release → HTTP 409
POST approve-release → ReleaseApprovedAtUtc set
POST release → released=16
```

测试后已把 16 条 Backlog 恢复为 Hold，并清空测试用启动审批。

构建：

```text
dotnet build backend/AgentOS.slnx → 0 errors
AgentOS tests → 307 passed / 64 skipped / 0 failed
frontend build → 1731 modules transformed
Playwright → Token 预算和“批准启动”可见，0 page errors
```

## 6. 安全

- 启动审批与计划批准分离：批准方案不等于批准执行。
- 二次审批默认开启。
- 修订后自动失效启动审批。
- release 仍只改 Hold → Pending，不直接执行任务。

## 7. 已知限制

- Token 使用量可能是估算值。
- 没有货币成本换算（模型单价）。
- 只保留上一版，没有完整版本列表。
- 二次审批没有记录审批人身份。
- 任务级依赖视图尚未实现。

## 8. 后续

1. 模型单价与费用估算。
2. 完整 Plan 版本历史。
3. 审批人身份/审计记录。
4. 任务级依赖与关键路径。
5. 按 Backlog 子集启动。
