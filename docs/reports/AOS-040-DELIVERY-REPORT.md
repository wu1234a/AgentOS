# AOS-040 Delivery Report：失败 Backlog 自动修复

- 日期：2026-11-11
- 状态：Completed
- 范围：失败任务 → repair item → 自动执行

## 1. 变更文件

- `backend/AgentOS.Domain/Backlog/BacklogItem.cs`
- `backend/AgentOS.Api/Backlog/BacklogOptions.cs`
- `backend/AgentOS.Api/Backlog/BacklogWorker.cs`
- `backend/AgentOS.Api/appsettings.json`
- `backend/AgentOS.Infrastructure/Persistence/Configurations/BacklogItemConfiguration.cs`
- `backend/AgentOS.Infrastructure/Persistence/AgentOSDbContext.cs`
- `backend/AgentOS.Infrastructure/Migrations/20261111115707_AddBacklogRepair.cs`（新增）
- `backend/AgentOS.Infrastructure/Migrations/AgentOSDbContextModelSnapshot.cs`

文档：

- `tasks/AOS-040-backlog-auto-repair.md`（新增）
- `docs/reports/AOS-040-DELIVERY-REPORT.md`（本文件）

## 2. 行为

任务进入 `Failed` 且原 item 的 `RepairAttempt` 小于配置上限时：

1. 原 item 标记 `RepairScheduled`，保存失败原因。
2. 创建新的 Pending item：
   - 标题追加 `(repair)`；
   - `RepairAttempt = 原值 + 1`；
   - `SourceItemId = 最初 item`；
   - instruction 追加失败原因；
   - validation/delivery 继承；
   - delivery 分支改为 `aos/backlog-{newItemId:N}`。
3. 队列空闲时 repair item 自动派发。
4. Repair 再失败且达到上限，标记 `TaskFailed`，不再创建下一条。

配置：

```json
"Backlog": {
  "Enabled": true,
  "PollSeconds": 15,
  "MaxRepairAttempts": 1
}
```

## 3. 验证

构造一条必定失败的 Backlog：

```text
validation: dotnet build missing-file.slnx
```

执行结果：

```text
原始 item:  01a1ca78-b4e6-72e7-b40c-3415a4c4b654
状态:       RepairScheduled
错误:       Validation command failed with exit code 1.

repair item: 01a1ca7a-71b0-7848-829d-e3b391bc8d71
RepairAttempt: 1
状态:       TaskFailed
```

说明：

- 第一条失败没有直接终止，而是自动派生 repair。
- repair 使用新的分支，不会覆盖原任务分支。
- repair 再次失败后正确停止，没有无限循环。
- 3dERP main 保持 `d32b4d5`，失败测试没有产生代码变更。

## 4. 验证命令

```text
dotnet build backend/AgentOS.slnx → 0 errors
AgentOS tests → 301 passed / 64 skipped / 0 failed
```

## 5. 安全

- 只在任务进入 Failed 时修复，Cancelled/Rejected 不触发。
- 最大修复次数可配置，默认 1。
- Repair 继承原项目、优先级和受控 delivery 字段。
- 新分支由新 item id 生成，不覆盖已有分支。
- 不执行任意 shell；校验命令仍来自受控 payload。

## 6. 已知限制

- 只包含任务 ErrorCode/ErrorMessage，不包含完整运行日志。
- 只有一级 repair，没有多级修复链。
- Repair 失败后需要人工处理或新建 backlog。
- 没有前端展示 repair 链。
- 仍依赖原任务的 validation/delivery 配置正确。

## 7. 后续

1. 把任务日志摘要注入 repair instruction。
2. 支持多级 repair 和预算限制。
3. Repair 失败自动进入 dead-letter/人工队列。
4. Backlog/repair 前端可视化。
5. Planner 批量生成 backlog。
