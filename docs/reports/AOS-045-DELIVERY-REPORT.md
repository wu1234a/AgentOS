# AOS-045 Delivery Report：多 Agent 项目立项与路线图 MVP

- 日期：2026-11-13
- 状态：Completed
- 范围：项目级多 Agent 规划 → 人工批准 → Backlog

## 1. 变更文件

后端：

- `backend/AgentOS.Domain/Planning/ProjectPlan.cs`（新增）
- `backend/AgentOS.Infrastructure/Persistence/Configurations/ProjectPlanConfiguration.cs`（新增）
- `backend/AgentOS.Infrastructure/Persistence/AgentOSDbContext.cs`
- `backend/AgentOS.Infrastructure/Migrations/20261112202511_AddProjectPlans.cs`（新增）
- `backend/AgentOS.Domain/Backlog/BacklogItem.cs`
- `backend/AgentOS.Infrastructure/Persistence/Configurations/BacklogItemConfiguration.cs`
- `backend/AgentOS.Api/ProjectPlanning/ProjectPlanModelRunner.cs`（新增）
- `backend/AgentOS.Api/ProjectPlanning/ProjectPlanService.cs`（新增）
- `backend/AgentOS.Api/ProjectPlanning/ProjectPlanWorker.cs`（新增）
- `backend/AgentOS.Api/Endpoints/ProjectPlanEndpoints.cs`（新增）
- `backend/AgentOS.Api/Backlog/BacklogService.cs`
- `backend/AgentOS.Api/Program.cs`
- `backend/AgentOS.Api/appsettings.json`
- `backend/AgentOS.Application/Planning/PlannerContracts.cs`

测试：

- `tests/AgentOS.Api.Tests/ProjectPlanServiceTests.cs`（新增）

文档：

- `tasks/AOS-045-multi-agent-project-planning.md`（新增）
- `docs/reports/AOS-045-DELIVERY-REPORT.md`（本文件）

## 2. 规划流程

```text
POST /project-plans
 → Queued
 → ProjectPlanWorker picks
 → 3 个角色独立提案（Product / Architecture / Cost-Risk）
 → 3 个角色交叉评审
 → Chair 综合
 → PendingReview
 → 人工 approve
 → 16 条 Backlog（PlanId + EpicKey）
```

模型调用上限：7 次（3 + 3 + 1）。使用 `deepseek-reviewer` 作为项目规划首选 profile，Kimi 作为后续候选。

## 3. 输出结构

Chair 必须返回：

```json
{
  "recommendedOption": "...",
  "rationale": "...",
  "constraints": ["..."],
  "roadmap": [{"milestone":"M1","goal":"...","exitCriteria":"..."}],
  "risks": [{"risk":"...","mitigation":"..."}],
  "acceptanceMetrics": ["..."],
  "tasks": [
    {"kind":"backend|frontend|docs|full","epic":"M1","title":"...","instruction":"..."}
  ]
}
```

批准时只接受 `kind`、标题、指令和 Epic 合法的任务，最多 20 条。

## 4. 端到端验证

创建计划：

```text
plan: 01a1d175-2f3c-7e83-ae4f-fad8c65719fa
title: 3dERP 报表模块立项 v2
```

执行耗时约 3 分钟：

```text
Round 1: product / architecture / cost-risk  ~1800 chars each
Round 2: 3 critiques                         ~1000-1600 chars each
Round 3: chair synthesis                     ~5900 chars
```

Chair 输出：

```text
recommendedOption: 轻量只读库存报表垂直切片
roadmap: M1 盘点/口径 → M2 后端 API → M3 前端 → M4 采购/销售扩展
tasks: 16
```

批准：

```text
POST /project-plans/{id}/approve → Approved
BacklogItems: 16
PlanId: 01a1d175-2f3c-7e83-ae4f-fad8c65719fa
EpicKey: M1/M2/M3/M4
```

为避免演示计划立即执行，16 条 Backlog 已置为 `Hold`；真正执行时将其恢复为 `Pending`。

## 5. 验证

```text
dotnet build backend/AgentOS.slnx → 0 errors
AgentOS tests → 307 passed / 64 skipped / 0 failed
```

## 6. 安全与成本

- 规划最多 7 次模型调用，不会无限辩论。
- 目标/约束作为不可信数据处理。
- Chair 输出经过 JSON、kind、长度校验。
- 批准前不会创建 Backlog。
- 批准后任务仍需经过 Runner 隔离、校验、PR、评审、合并。
- 没有真实费用预算控制，仅有调用次数上限。

## 7. 已知限制

- 没有前端立项评审页面。
- 规划 profile 固定为配置值，不支持请求级选择。
- 没有多轮追问/人工插入意见。
- 规划失败后不会自动重试整个 board，只能新建计划。
- Backlog 目前只能用 SQL 从 Hold 恢复为 Pending，尚无 API。
- 规划结果质量依赖模型，需要人工批准。

## 8. 后续

1. 前端 Project Plan 评审页。
2. 支持人工补充意见并触发第二轮。
3. 费用/Token 预算上限。
4. Plan 版本和修订。
5. Hold/Pending 的 API。
6. 自动把批准计划送入执行并关联里程碑完成度。
