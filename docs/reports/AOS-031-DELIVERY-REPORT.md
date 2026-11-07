# AOS-031 交付报告：Worker 管理

**状态：Completed（第一阶段）**
**完成时间：2026-11-08**
**任务文件：** `tasks/AOS-031-worker-admin.md`

## 1. 能力

- Worker 列表（含运行任务数）、统计、启用/禁用。
- 禁用 Worker 不领取新任务（Claimer 既有检查）。
- 无迁移。

## 2. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **322 通过 / 5 跳过 / 0 失败** |
| Worker 用例 | ✅ 列表 + 禁用 |
| CI | 见对应 run |

## 3. 已知限制

1. 无自动扩缩容/负载均衡/Worker 标签。
2. 无前端/审计。
3. 禁用不迁移运行中任务。
4. 运行任务数按 LeaseOwnerWorkerId 统计，非实时心跳。

## 4. 变更文件

`WorkerAdminService`、`WorkerAdminEndpoints`、Program、`SqlWorkerAdminTests.cs`、文档。
