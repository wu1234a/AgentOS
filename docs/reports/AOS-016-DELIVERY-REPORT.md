# AOS-016 交付报告：任务依赖与阻塞状态

**状态：Completed（第一阶段）**
**完成时间：2026-11-07**
**任务文件：** `tasks/AOS-016-dependencies.md`

## 1. 能力

- `TaskStatus.Blocked` 与 `TaskDependencies` 表（迁移 `AddTaskDependencies`）。
- Claimer 在候选查询和原子领取 SQL 中排除依赖未全部 Succeeded 的任务。
- `TaskDependencyCoordinator` 每轮 LeaseRecovery：
  - 前置全部成功 → 后继 Blocked → Queued；
  - 前置 Failed/Cancelled → 后继 Failed（`dependency-failed`），最多 20 轮传递。
- 无依赖任务的既有流程不变。

## 2. 数据与 API

- 迁移 `AddTaskDependencies`：复合主键 `(TaskId, PrerequisiteTaskId)`、双 FK Restrict、
  前置任务索引。
- 未新增公共 API；依赖写入/子任务 API 在下一阶段。

## 3. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **304 通过 / 5 跳过 / 0 失败** |
| 依赖用例 | ✅ 释放 B、保持 C Blocked、B 失败后 C `dependency-failed` |
| CI | 见对应 run |

## 4. 已知限制

1. 无子任务创建/列表 API、父子汇总。
2. 无阶段字段、状态审计表、CodingRun 四阶段推进。
3. 无前端、依赖图编辑、环检测（依赖只能直接写库，后续 API 补校验）。
4. 失败传播依赖 LeaseRecovery 周期，不是即时事务钩子。
5. 跨项目/权限边界校验留给依赖 API。

## 5. 变更文件

`TaskStatus.cs`、`TaskDependency.cs`、依赖配置、`AgentOSDbContext`、`TaskClaimer.cs`、
`TaskDependencyCoordinator.cs`、`ITaskDependencyCoordinator.cs`、`LeaseRecoveryService.cs`、
`DependencyInjection.cs`、迁移 `AddTaskDependencies`、测试、文档。
