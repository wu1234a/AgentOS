# AOS-021 交付报告：Decision Memory

**状态：Completed**
**完成时间：2026-11-07**
**任务文件：** `tasks/AOS-021-decision-memory.md`

## 1. 能力

- 审批决策成功后自动写 `DecisionMemories`（来源审批唯一、90 天有效）。
- 列表/详情/软失效 API。
- `GET /api/v1/approval-requests/{id}/decision-memory-references`：同 scope active 记忆，最多 5 条，返回时命中次数 +1，附免责声明。
- 迁移 `AddDecisionMemories`。

## 2. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **312 通过 / 5 跳过 / 0 失败** |
| Decision Memory 用例 | ✅ 自动沉淀、参考命中、失效排除 |
| CI | 见对应 run |

### 2.1 独立代码审查\n\nCodex CLI 审查发现 2 个 P2（命中计数并发丢失、失效审计覆盖），已改为数据库条件更新，详见 `docs/reports/AOS-021-CODE-REVIEW.md`。\n\n## 3. 已知限制

1. 无补偿创建/批量回填。
2. 无前端历史参考区域。
3. 无自动批准/执行（符合设计）。
4. 作用域仅为 TargetType/TargetId，无项目/标签维度。
5. 命中计数为读时更新，极端并发可能少计。

## 4. 变更文件

`DecisionMemory.cs`、配置、迁移、`DecisionContracts/Service`、`DecisionMemoryStore`、
`ApprovalService`、`DecisionMemoryEndpoints`、DI/Program、测试、文档。
