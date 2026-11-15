# AOS-055 Delivery Report：任务文档、里程碑与人工验收门

- 日期：2026-11-15
- 状态：Completed（MVP）
- 范围：Phase 3 任务文档/角色元数据 + Phase 5 里程碑/验收门

## 1. 变更文件

后端：

- `backend/AgentOS.Domain/Backlog/BacklogItem.cs`
- `backend/AgentOS.Domain/Projects/ProjectMilestone.cs`（新增）
- `backend/AgentOS.Infrastructure/Persistence/Configurations/BacklogItemConfiguration.cs`
- `backend/AgentOS.Infrastructure/Persistence/Configurations/ProjectMilestoneConfiguration.cs`（新增）
- `backend/AgentOS.Infrastructure/Persistence/AgentOSDbContext.cs`
- `backend/AgentOS.Infrastructure/Migrations/20261115110643_AddTaskDocsMilestones.cs`（新增）
- `backend/AgentOS.Api/Backlog/BacklogService.cs`
- `backend/AgentOS.Api/ProjectPlanning/ProjectPlanService.cs`
- `backend/AgentOS.Api/ProjectPlanning/ProjectPlanWorker.cs`
- `backend/AgentOS.Api/ProjectDocuments/ProjectMilestoneService.cs`（新增）
- `backend/AgentOS.Api/ProjectDocuments/ProjectDocumentService.cs`
- `backend/AgentOS.Api/Endpoints/ProjectMilestoneEndpoints.cs`（新增）
- `backend/AgentOS.Api/Program.cs`
- `backend/AgentOS.Application/Projects/ProjectService.cs`

前端：

- `frontend/agentos-web/src/api/backlog.js`（新增）
- `frontend/agentos-web/src/api/projectMilestones.js`（新增）
- `frontend/agentos-web/src/views/ProjectsView.vue`

文档：

- `tasks/AOS-055-task-docs-milestones-acceptance.md`（新增）
- `docs/reports/AOS-055-DELIVERY-REPORT.md`（本文件）

## 2. 任务文档

BacklogItem 新增：

```text
RequiredRole
RiskLevel
TaskDocumentMarkdown
```

计划批准时每个任务生成 Markdown：

```markdown
# T1 任务标题
- 项目 / 计划 / Epic / Kind
- RequiredRole
- RiskLevel
- 依赖
## 目标
## 验收标准
## 实现记录
```

计划 Chair schema 支持：

```json
{
  "requiredRole": "architect|coder|reviewer|security|performance|qa|writer",
  "riskLevel": "low|normal|high"
}
```

当前 RequiredRole/RiskLevel 只记录和展示，尚未按角色分派 Agent。

## 3. 里程碑

`ProjectMilestones`：

```text
Id, ProjectId, PlanId, Key, Title, Goal, ExitCriteria,
Status = Pending/Verified, VerifiedAtUtc, VerifiedBy
```

计划批准时从 final plan 的 `roadmap` 生成。

API：

```text
GET  /api/v1/projects/{projectId}/milestones
POST /api/v1/projects/{projectId}/milestones/{milestoneId}/verify
POST /api/v1/projects/{projectId}/accept
```

- 人工 verify 记录 `VerifiedBy`。
- 全部 Verified 后 Project.Status = Acceptance。
- `/accept` 是最终人工验收门：生成 ACCEPTANCE-REPORT，项目 Completed。

## 4. 前端

项目详情新增：

- 里程碑表，人工验证
- 任务文档表，查看 Markdown
- 全部里程碑验证后显示“最终验收”
- 最终验收按钮不会自动点击

验证流程停在最终验收前，等待人工。

## 5. 验证

使用合成计划验证：

```text
Approve
→ 2 个任务文档生成（含角色/风险/依赖）
→ 2 个里程碑 Pending
→ 验证 M1/M2
→ Project.Status = Acceptance
→ 未调用 /accept
```

测试数据已清理，3dERP 项目恢复 Active。

```text
dotnet build → 0 errors
AgentOS tests → 309 passed / 64 skipped / 0 failed
frontend build → 1735 modules transformed
Playwright → 里程碑和最终验收按钮可见，0 page errors
```

## 6. 已知限制

- RequiredRole 只展示，未按角色分派 Agent。
- Security/Performance 仍是 Review Agent 的审查范围，没有独立 Agent。
- 里程碑验证是人工操作，不自动按 Backlog 完成度判定。
- Plan 修订不会重建里程碑。
- 最终验收报告是 Markdown，没有审批数字签名。
- 验收 API 未实际调用（按用户要求停在人工验收前）。

## 7. 下一步

Phase 4 深化：

- 独立 Security/Performance/QA 角色。
- 任务级证据（测试/审查/性能）。
- 验收标准自动核对。
- 角色队列。

Phase 5 后续：

- 里程碑自动完成度。
- 验收报告模板/签名。
- 项目归档。
