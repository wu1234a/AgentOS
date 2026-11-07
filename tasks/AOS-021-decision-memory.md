# AOS-021：Decision Memory 决策记忆

状态：Completed
优先级：P1
依赖：AOS-018、AOS-020
来源：Kimi Planner 草案 `01a1b67a-56b7-7d87-9058-be71098ae4f2`（已批准）

## 0. 修订

Kimi 草案含补偿创建、审批参考前端、复杂作用域。本阶段先交付：

1. `DecisionMemories` 表 + 审批决策自动沉淀。
2. 列表/详情/失效 API + 审批参考 API（只读，最多 5 条）。
3. 生命周期由时间/失效字段计算；命中次数原子递增。
4. 不做补偿创建、独立管理页、前端参考区域。

## 1. 范围

- 决策成功（Approved/Rejected）自动写记忆，来源审批唯一；90 天有效期。
- `GET /api/v1/decision-memories`（decision/lifecycle/scope/分页）。
- `GET /{id}`。
- `POST /{id}/invalidate`（理由必填、软失效、幂等）。
- `GET /api/v1/approval-requests/{id}/decision-memory-references`：
  - 同 scope active 记忆、按决策时间倒序、最多 5 条；
  - 返回时命中次数 +1；
  - 明确 disclaimer，不自动审批。
- 迁移 `AddDecisionMemories`。

## 2. 不在范围

- 自动批准/拒绝/执行、语义检索/embedding、多租户。
- 补偿创建、批量回填、恢复/物理删除、独立管理页、前端参考区域。

## 3. 安全

- 沿用开发 Key；参考接口只读（除命中计数）。
- 理由/失效原因长度限制；软失效保留审计。
- 记忆失败不回滚审批结果。

## 4. 验收

- [x] 审批决策自动生成记忆，来源唯一。
- [x] 参考仅返回 active 同 scope，命中计数递增。
- [x] 失效后从参考排除。
- [x] 后端全量测试、CI 通过。
