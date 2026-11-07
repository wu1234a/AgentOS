# AOS-030 交付报告：死信与自动恢复

**状态：Completed（第一阶段）**
**完成时间：2026-11-08**
**任务文件：** `tasks/AOS-030-dead-letter.md`

## 1. 能力

- 死信列表：Failed/Blocked/Rejected，支持 projectId/status/limit。
- 人工 retry/takeover：重置 Queued、AttemptCount、lease/error。
- 人工 cancel。
- 自动恢复：Failed 超 15 分钟、RecoveryCount<3，每 5 分钟最多 20 个。
- 迁移 `AddTaskRecoveryCount`。

## 2. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **321 通过 / 5 跳过 / 0 失败** |
| 死信用例 | ✅ 列表 + retry 回 Queued |
| CI | 见对应 run |

### 2.1 独立代码审查\n\nCodex CLI 审查发现 1 个 P1（attempt 编号冲突）与 2 个 P2（RecoveryCount 初值、cancel 运行中任务），全部修复，详见 `docs/reports/AOS-030-CODE-REVIEW.md`。\n\n## 3. 已知限制

1. 无独立死信表/消息队列。
2. 无审计事件表；仅日志。
3. 自动恢复仅 Failed，不处理 Blocked/Rejected。
4. 无前端/告警。
5. 自动恢复上限硬编码 3 次/15 分钟/20 个。

## 4. 变更文件

`AutomationTask`、迁移、`DeadLetterService`、`DeadLetterEndpoints`、
`DeadLetterRecoveryWorker`、Program、`SqlDeadLetterTests.cs`、文档。
