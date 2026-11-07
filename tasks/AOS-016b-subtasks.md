# AOS-016b：子任务 API 与阶段键

状态：Completed
优先级：P1
依赖：AOS-016
来源：Kimi Planner 草案 `01a1b597-329a-7694-9687-71eee23399c4`（已批准）

## 0. 修订

Kimi 草案含状态审计表、CodingRun 薄适配器与幂等重试；本阶段先交付：

1. 子任务创建/列表 API、阶段键唯一、依赖初始化与父任务汇总。
2. 不新增审计表、不做 CodingRun 自动阶段推进、不做前端。
3. 创建幂等：同 phaseKey 已存在直接 409（后续补相同内容返回 200）。

## 1. 范围

- `AutomationTask.PhaseKey`（≤64）+ `(ParentTaskId, PhaseKey)` 唯一过滤索引。
- `POST /api/v1/tasks/{parentId}/subtasks`、`GET .../subtasks`。
- 校验：父任务存在且非嵌套/终态；标题 3–200；phaseKey 格式；依赖 ≤50、去重、存在、
  同父任务项目；不允许自依赖。
- 初始状态：依赖全部 Succeeded → Queued，否则 Blocked。
- 列表返回子任务、依赖 ID、父状态与汇总（total/queued/running/blocked/succeeded/failed）。
- 创建与依赖同事务保存。

## 2. 不在范围

- 状态迁移审计表、状态历史 API。
- CodingRun 阶段自动创建/推进、自动完成父任务。
- 前端、依赖图/环检测 API、跨父流程依赖。
- 幂等内容重复请求返回 200（当前重复 phaseKey 统一 409）。

## 3. 安全

- 复用开发 Key 认证；父任务/依赖必须同项目（依赖存在性校验）。
- 客户端不能指定 ProjectId/状态/Agent；继承父任务。
- phaseKey/标题/依赖数量限制，防止滥用。

## 4. 验收

- [x] 创建子任务，继承项目/Agent，依赖未满足时 Blocked。
- [x] 无依赖子任务 Queued；phaseKey 重复 409。
- [x] 列表返回子任务与汇总。
- [x] 后端全量测试、CI 通过。
