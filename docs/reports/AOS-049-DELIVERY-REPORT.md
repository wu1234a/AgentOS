# AOS-049 Delivery Report：立项版本历史与审批审计

- 日期：2026-11-13
- 状态：Completed
- 范围：完整版本快照、版本列表/查看、审批操作者审计

## 1. 变更文件

后端：

- `backend/AgentOS.Domain/Planning/ProjectPlanRevision.cs`（新增）
- `backend/AgentOS.Domain/Planning/ProjectPlan.cs`
- `backend/AgentOS.Infrastructure/Persistence/Configurations/ProjectPlanRevisionConfiguration.cs`（新增）
- `backend/AgentOS.Infrastructure/Persistence/Configurations/ProjectPlanConfiguration.cs`
- `backend/AgentOS.Infrastructure/Persistence/AgentOSDbContext.cs`
- `backend/AgentOS.Infrastructure/Migrations/20261113143915_AddProjectPlanRevisions.cs`（新增）
- `backend/AgentOS.Api/ProjectPlanning/ProjectPlanService.cs`
- `backend/AgentOS.Api/ProjectPlanning/ProjectPlanWorker.cs`
- `backend/AgentOS.Api/Endpoints/ProjectPlanEndpoints.cs`

前端：

- `frontend/agentos-web/src/api/projectPlans.js`
- `frontend/agentos-web/src/views/ProjectPlansView.vue`

文档：

- `tasks/AOS-049-plan-revisions-audit.md`（新增）
- `docs/reports/AOS-049-DELIVERY-REPORT.md`（本文件）

## 2. 版本快照

`ProjectPlanRevisions`：

```text
Id
PlanId
Revision
FinalPlanJson
Feedback
ModelProfileCode
TokensUsed
EstimatedCost
CreatedAtUtc
```

- 规划完成进入 PendingReview 时写入。
- `(PlanId, Revision)` 唯一，重复生成不会重复插入。
- 修订后 Revision+1，下次生成写入新快照。
- 已存在的计划已回填当前最终方案为 v1。

API：

```text
GET /api/v1/project-plans/{id}/revisions
```

前端：

- “版本历史”标签页
- 显示版本、模型、Tokens、费用、人工意见、时间
- 可查看任意版本 JSON

## 3. 审批审计

字段：

```text
DecidedBy
ReleaseApprovedBy
```

- 来源：`X-AgentOS-Actor` 请求头
- 前端决策请求默认发送 `web-admin`
- 缺省值 `api-key`
- 修订后清空 `DecidedBy`、`ReleaseApprovedBy`

实测：

```text
X-AgentOS-Actor: tester
POST /approve-release
→ releaseApprovedBy = tester
```

这是审计标识，不是强身份认证。

## 4. 验证

```text
GET revisions → v1 deepseek-reviewer
POST approve-release with actor → releaseApprovedBy=tester
Playwright → 版本历史标签可见、0 page errors
dotnet build → 0 errors
AgentOS tests → 307 passed / 64 skipped / 0 failed
frontend build → 1731 modules transformed
```

## 5. 安全说明

- `X-AgentOS-Actor` 可伪造，仅用于操作审计。
- 真正的身份认证仍需接入登录/API 客户端身份。
- 版本快照包含计划全文，按现有管理 API 权限保护。

## 6. 已知限制

- 旧计划只有回填的 v1，没有历史回填。
- 版本对比是人工查看 JSON，没有 diff 视图。
- 操作者没有用户 ID/角色。
- 任务级依赖尚未在执行层强制。
- 没有按 Epic 批量启动。

## 7. 后续

1. 登录身份接入，替换 X-AgentOS-Actor。
2. 版本 JSON diff 视图。
3. 执行层依赖阻塞。
4. 按 Epic 批量启动。
5. 模型级单价。
