# AOS-003：Event Deduplication & Idempotency

状态：Not Started  
优先级：P0  
依赖：AOS-002

## 1. 目标

为 Event Hub 增加可靠的事件去重和幂等行为，确保网络重试、Webhook
重发不会导致重复事件记录或重复后续任务。

## 2. 数据库变更

- 为 `Events` 增加 `DedupeKey`
  必填约束（如旧数据存在，先制定安全回填方案）。
- 建立 `Source + DedupeKey` 唯一索引。
- 如数据库实际采用不同的唯一策略，必须在文档中解释并覆盖测试。
- 可增加 `PayloadHash`，用于检测相同去重键但不同内容的冲突。
- 不得删除已有 Event 数据。

## 3. 行为定义

同一 `Source + DedupeKey`： 1. 首次请求：创建 Event，返回
`duplicate: false`。 2. 再次提交相同内容：返回原
EventId，`duplicate: true`。 3.
再次提交不同内容：不得覆盖原事件；返回明确冲突结果并记录安全日志。 4.
并发提交：数据库唯一约束保证只产生一条有效 Event。

建议响应： - 新事件：202 Accepted。 - 已存在且内容一致：200 OK 或
202，保持 API 文档统一。 - 去重键冲突：409 Conflict。

## 4. 幂等任务预留

AOS-004 将根据 Event 创建 Task，因此本任务需要在数据模型/设计中明确：
`EventId + RuleId + RuleVersion + DispatchKey` 是后续逻辑任务幂等键。

本任务不负责生成 Task，但不得设计出无法实现该约束的结构。

## 5. 实施要求

- 应用层可以先尝试读取，但必须依赖数据库唯一索引兜底。
- 捕获 SQL Server 唯一约束冲突，并转换成稳定 API 响应。
- 不允许使用“先查询是否存在，然后无事务插入”作为唯一防线。
- PayloadHash 计算使用规范化 JSON 或原始 Body
  的明确策略；策略必须固定并写入文档。
- 对重复请求的日志进行采样或聚合，避免大量重试刷屏。

## 6. 验收标准

- [ ] 相同事件重复提交只保留一条 Event。
- [ ] 重复请求返回相同 EventId。
- [ ] 相同 DedupeKey 不同 Payload 不会覆盖原数据。
- [ ] 并发提交测试最终只产生一条记录。
- [ ] 数据库唯一约束真实存在。
- [ ] 旧事件数据迁移安全。
- [ ] API 文档明确重复事件响应行为。

## 7. 交付

创建
`docs/reports/AOS-003-DELIVERY-REPORT.md`，附唯一索引定义和并发测试结果。
