# AOS-050 Delivery Report：Backlog 执行层依赖

- 日期：2026-11-13
- 状态：Completed
- 范围：计划任务依赖落到 Backlog，BacklogWorker 按依赖派发

## 1. 变更文件

后端：

- `backend/AgentOS.Domain/Backlog/BacklogItem.cs`
- `backend/AgentOS.Infrastructure/Persistence/Configurations/BacklogItemConfiguration.cs`
- `backend/AgentOS.Infrastructure/Migrations/20261113150232_AddBacklogDependencies.cs`（新增）
- `backend/AgentOS.Api/Backlog/BacklogService.cs`
- `backend/AgentOS.Api/Backlog/BacklogWorker.cs`
- `backend/AgentOS.Api/ProjectPlanning/ProjectPlanService.cs`

文档：

- `tasks/AOS-050-backlog-dependencies.md`（新增）
- `docs/reports/AOS-050-DELIVERY-REPORT.md`（本文件）

## 2. 数据

BacklogItem 新增：

```text
TaskKey
DependsOnJson
```

批准计划时写入：

```text
TaskKey      = plan task taskKey
DependsOnJson = ["T1", "T2"]
```

## 3. 派发规则

BacklogWorker 对每个 Pending 项：

- 无 PlanId / 无依赖：直接可派发。
- 依赖项不存在：`NeedsAttention`。
- 依赖项失败/取消/NeedsAttention：`NeedsAttention`。
- 依赖项未 Completed：继续等待。
- 全部 Completed：允许派发。

状态：

```text
Pending → Dispatched → Completed
Pending → NeedsAttention（依赖阻断）
```

## 4. 兼容性

- 旧 Backlog 的 TaskKey/DependsOnJson 为 null，行为与之前一致。
- 现有 16 条报表 Backlog 无依赖数据，不受影响。

## 5. 验证

```text
dotnet build backend/AgentOS.Api → 0 errors
EF migration AddBacklogDependencies → applied
AgentOS tests → 306 passed / 64 skipped / 1 flaky
flaky rerun → 5/5 passed
```

## 6. 已知限制

- 没有依赖图环检测；只按声明的前序 key 等待。
- 没有关键路径计算。
- 依赖失败需要人工/后续修复，不自动重排。
- 没有前端 Backlog 依赖明细（计划任务表已有依赖列）。

## 7. 后续

1. 依赖环检测和关键路径。
2. 依赖失败自动重试/重排。
3. Backlog 依赖 UI。
4. 按 Epic 批量启动。
