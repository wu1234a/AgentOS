# AOS-034 交付报告：流水线只读视图

**状态：Completed**
**完成时间：2026-11-08**
**任务文件：** `tasks/AOS-034-pipeline.md`

## 1. 能力

- `GET /api/v1/pipelines`：任务摘要（状态/attempt 数/是否有 CodingRun）。
- `GET /api/v1/pipelines/tasks/{id}`：task、attempt、coding-run、review、qa 阶段。
- 无迁移。

## 2. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **326 通过 / 5 跳过 / 0 失败** |
| 流水线用例 | ✅ 任务阶段聚合 |
| CI | 见对应 run |

### 2.1 独立代码审查\n\nCodex CLI 审查发现 3 个 P2（CodingRun 状态滞后、QA 结论缺失、Review 时间戳），全部修复，详见 `docs/reports/AOS-034-CODE-REVIEW.md`。\n\n## 3. 已知限制

1. 无前端图组件/实时推送。
2. Review/QA 阶段无开始/完成时间（实体无字段）。
3. 一次查询存在多次子查询（N+1 风险）。
4. 无跨任务依赖 DAG。

## 4. 变更文件

`PipelineService`、`PipelineEndpoints`、Program、`SqlPipelineTests.cs`、文档。
