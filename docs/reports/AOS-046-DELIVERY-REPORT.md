# AOS-046 Delivery Report：立项后续控制

- 日期：2026-11-13
- 状态：Completed
- 范围：执行启动、Epic 进度、人工反馈修订

## 1. 变更文件

后端：

- `backend/AgentOS.Domain/Planning/ProjectPlan.cs`
- `backend/AgentOS.Infrastructure/Persistence/Configurations/ProjectPlanConfiguration.cs`
- `backend/AgentOS.Infrastructure/Migrations/20261113063411_AddProjectPlanRevision.cs`（新增）
- `backend/AgentOS.Infrastructure/Migrations/AgentOSDbContextModelSnapshot.cs`
- `backend/AgentOS.Api/ProjectPlanning/ProjectPlanService.cs`
- `backend/AgentOS.Api/ProjectPlanning/ProjectPlanWorker.cs`
- `backend/AgentOS.Api/Endpoints/ProjectPlanEndpoints.cs`

前端：

- `frontend/agentos-web/src/api/projectPlans.js`
- `frontend/agentos-web/src/views/ProjectPlansView.vue`

文档：

- `tasks/AOS-046-plan-execution-control.md`（新增）
- `docs/reports/AOS-046-DELIVERY-REPORT.md`（本文件）

## 2. 新 API

```text
POST /api/v1/project-plans/{id}/release
GET  /api/v1/project-plans/{id}/progress
POST /api/v1/project-plans/{id}/revise
```

### release

- 计划必须是 `Approved`。
- 只把该 PlanId 下 `Hold` 的 Backlog 改成 `Pending`。
- 返回：

```json
{ "released": 16, "plan": { "status": "Approved" } }
```

### progress

按 EpicKey 汇总：

```json
{
  "total": 16,
  "epics": [
    {"epicKey":"M1","total":3,"pending":0,"hold":3,"active":0,"completed":0,"failed":0}
  ]
}
```

### revise

- 只允许 `PendingReview` 或 `Failed`。
- 反馈长度 5-8000。
- `Revision++`，清空旧最终方案，状态回到 `Queued`。
- Worker 下一轮在提案、评审、综合 prompt 中加入 `<humanFeedback>`。

## 3. 前端

- 详情页新增“执行进度”表。
- Approved 且存在 Hold 时显示“启动执行（N）”。
- PendingReview/Failed 显示“人工修订意见”输入框和“按意见重新规划”。
- 显示修订版本和人工意见。

## 4. 验证

```text
GET progress → total 16, M1=3, M2=4, M3=3, M4=6, all Hold
POST release → released 16
POST release 后恢复 Hold → 16 Hold
POST revise on Approved → HTTP 409（修复后）
Playwright → 执行进度可见、启动执行按钮可见、0 page errors
```

后端构建和测试：

```text
dotnet build backend/AgentOS.slnx → 0 errors
AgentOS tests → 307 passed / 64 skipped / 0 failed
```

## 5. 安全

- release 不直接执行任务，只改 Backlog 状态。
- 启动后仍受 Runner 隔离、校验、PR、评审、合并约束。
- revise 不会修改已批准计划；已批准只能新建或后续扩展。
- 人工意见按不可信输入拼入 prompt，无指令提升。

## 6. 已知限制

- Release 没有二次权限审批，仅前端确认。
- 没有按单条 Backlog 选择启动，只支持整个计划。
- 修订会丢弃当前最终方案草稿（历史贡献保留）。
- 没有真实 Token/费用预算。
- 进度只按 Epic 统计，没有任务级依赖图视图。

## 7. 后续

1. 选择部分 Backlog 启动。
2. 费用/Token 预算。
3. Plan 版本历史对比。
4. 任务级依赖与里程碑完成度。
5. 高风险启动审批。
