# AOS-016b 交付报告：子任务 API 与阶段键

**状态：Completed**
**完成时间：2026-11-07**
**任务文件：** `tasks/AOS-016b-subtasks.md`

## 1. 能力

- `AutomationTask.PhaseKey` + `(ParentTaskId, PhaseKey)` 唯一索引（迁移 `AddSubtaskPhaseKey`）。
- `POST /api/v1/tasks/{parentId}/subtasks`：
  - 校验父任务、标题、phaseKey、依赖（存在/数量/去重/自依赖）；
  - 继承父任务项目、Agent、优先级、超时；
  - 依赖全成功 → Queued，否则 Blocked；
  - phaseKey 重复 → 409。
- `GET /api/v1/tasks/{parentId}/subtasks`：子任务列表、依赖 ID、父状态与状态汇总。

## 2. 数据与 API

- 迁移 `AddSubtaskPhaseKey`：PhaseKey 列、过滤唯一索引。
- 无审计表、无状态历史 API、无 CodingRun 自动阶段推进。

## 3. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **306 通过 / 5 跳过 / 0 失败** |
| 子任务用例 | ✅ 依赖未满足 Blocked、列表汇总、phaseKey 冲突 |
| CI | 见对应 run |

## 4. 已知限制

1. 无状态迁移审计/历史 API。
2. 无 CodingRun 阶段自动创建/推进。
3. 无前端展示。
4. 重复 phaseKey 不区分内容是否相同，统一 409。
5. `UnresolvedDependencyIds` 当前与依赖列表相同（未按状态过滤，后续修复）。
6. 依赖环检测仅覆盖自依赖；新子任务尚不能被既有任务引用，故新增 API 不产生环，但通用校验器未实现。

## 5. 变更文件

`AutomationTask.cs`、任务配置、`SubtaskContracts.cs`、`SubtaskService.cs`、
`SubtaskRepository.cs`、`SubtaskEndpoints.cs`、DI/Program、迁移、`SqlSubtaskTests.cs`、文档。
