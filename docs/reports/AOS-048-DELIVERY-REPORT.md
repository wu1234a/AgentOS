# AOS-048 Delivery Report：子集启动、费用估算与任务依赖

- 日期：2026-11-13
- 状态：Completed
- 范围：按 Backlog 子集启动、Token 费用估算、任务依赖展示

## 1. 变更文件

后端：

- `backend/AgentOS.Application/Planning/PlannerContracts.cs`
- `backend/AgentOS.Infrastructure/Planning/ApiCompletionClient.cs`
- `backend/AgentOS.Domain/Planning/ProjectPlan.cs`
- `backend/AgentOS.Infrastructure/Persistence/Configurations/ProjectPlanConfiguration.cs`
- `backend/AgentOS.Infrastructure/Migrations/20261113071318_AddProjectPlanCost.cs`（新增）
- `backend/AgentOS.Api/ProjectPlanning/ProjectPlanService.cs`
- `backend/AgentOS.Api/ProjectPlanning/ProjectPlanWorker.cs`
- `backend/AgentOS.Api/Endpoints/ProjectPlanEndpoints.cs`

前端：

- `frontend/agentos-web/src/api/projectPlans.js`
- `frontend/agentos-web/src/views/ProjectPlansView.vue`

测试：

- `tests/AgentOS.Api.Tests/ProjectPlanServiceTests.cs`

文档：

- `tasks/AOS-048-subset-release-cost-dependencies.md`（新增）
- `docs/reports/AOS-048-DELIVERY-REPORT.md`（本文件）

## 2. 子集启动

```text
POST /api/v1/project-plans/{id}/release
{ "backlogItemIds": ["..."] }
```

- 不传 IDs：启动全部 Hold。
- 传 IDs：只启动属于该计划且状态为 Hold 的指定项。
- 任一 ID 不属于计划或不是 Hold：HTTP 400 `plan-items-not-held`。
- 仍需满足二次启动审批。

进度接口新增 `items`：

```json
{
  "items": [
    {"id":"...","title":"...","epicKey":"M1","status":"Hold"}
  ]
}
```

前端进度卡片增加 Backlog 明细表和复选框，支持“启动选中（N）”和“启动全部（N）”。

## 3. Token 费用估算

- `ProjectPlan.PromptTokens` / `CompletionTokens` / `EstimatedCost`。
- 估算价格配置：

```text
Planner:ProjectPlanInputPricePerMillion  = 0.5 USD
Planner:ProjectPlanOutputPricePerMillion = 2.0 USD
```

- 前端显示“估算费用 $x.xxxx”。
- 仅用于预算感知，不是供应商账单。

## 4. 任务依赖

Chair 输出 schema 增加：

```json
{"taskKey":"T1","dependsOn":[],"kind":"backend","epic":"M1","title":"...","instruction":"..."}
```

- `taskKey` 唯一，`dependsOn` 引用前序 key。
- 解析器保留任务依赖。
- 前端任务表显示 Key 和依赖列。
- 当前依赖仅展示，不在 Backlog/Runner 层强制阻塞。

## 5. 验证

```text
POST release {backlogItemIds:[one]} → released=1
恢复 Hold → 16 Hold
GET progress → items=16
Playwright → Backlog 明细、估算费用可见，0 page errors
```

构建/测试：

```text
dotnet build backend/AgentOS.slnx → 0 errors
AgentOS tests → 307 passed / 64 skipped / 0 failed
frontend build → 1731 modules transformed
```

## 6. 已知限制

- Token 费用是估算，不是精确账单。
- 任务依赖只展示，尚未映射到 TaskDependency。
- 没有关键路径算法。
- 已批准计划不能重新生成任务依赖，只能修订。
- 没有按 Epic 批量选择。

## 7. 后续

1. Backlog 依赖表和 Runner 阻塞。
2. 关键路径视图。
3. 按 Epic 选择启动。
4. 模型级单价配置。
5. 完整 Plan 版本历史。
