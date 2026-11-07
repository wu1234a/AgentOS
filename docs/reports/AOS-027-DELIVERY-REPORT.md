# AOS-027 交付报告：定时调度

**状态：Completed（间隔调度 MVP）**
**完成时间：2026-11-08**
**任务文件：** `tasks/AOS-027-scheduling.md`

## 1. 能力

- `ScheduledJobs`：间隔秒数 + 首次/下次运行时间。
- API：`/api/v1/scheduled-jobs` CRUD + enable/disable。
- 后台 Worker：每 15 秒扫描，调用 EventService 发布 `source=scheduler` 事件。
- 计划时间推进：`next = scheduledFor + (missed+1)*interval`，跳过错过的周期。
- 去重键：`schedule:<jobId>:<scheduledFor>`。
- 迁移 `AddScheduledJobs`。

## 2. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **319 通过 / 5 跳过 / 0 失败** |
| 调度用例 | ✅ 到期触发一次、NextRun 推进、重复扫描不再触发 |
| CI | 见对应 run |

### 2.1 独立代码审查\n\nCodex CLI 审查发现 1 个 P1（重试 payload 变化导致丢失）与 2 个 P2（eventType 长度、嵌套 payload 阻塞），全部修复，详见 `docs/reports/AOS-027-CODE-REVIEW.md`。\n\n## 3. 已知限制

1. 仅固定间隔；无 cron/日历/时区。
2. 单实例调度，无选主/分布式锁。
3. 错过多个周期只补发一次（按设计），但不保存 `missed_count` 到独立表。
4. 无前端管理页、手动补跑、删除后审计。
5. 发布成功但本地状态更新失败时，依赖事件 DedupeKey 防重复；未做独立触发记录表。
6. 真实并发 Worker 未压力测试。

## 4. 变更文件

`ScheduledJob.cs`、配置、迁移、`ScheduledJobService/Contracts`、`ScheduledJobRepository`、
`ScheduledJobEndpoints`、`ScheduledJobWorker`、DI/Program、`SqlScheduledJobTests.cs`、文档。
