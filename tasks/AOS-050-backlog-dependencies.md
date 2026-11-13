# AOS-050：Backlog 执行层依赖

状态：Completed
优先级：P1
依赖：AOS-048

## 1. 范围

- BacklogItem 记录 TaskKey / DependsOnJson。
- 计划批准时把 taskKey/dependsOn 写入 Backlog。
- BacklogWorker 只派发依赖已完成的 Pending 项。
- 依赖失败/取消时，下游标记 NeedsAttention，不盲目继续。
- 旧 Backlog 无依赖字段时保持原行为。

## 2. 行为

```text
A (Hold -> Pending)
B dependsOn A (Pending)
```

- A 完成前 B 不派发。
- A Completed 后 B 才可派发。
- A TaskFailed/Cancelled/NeedsAttention → B NeedsAttention。
- 依赖缺失或 JSON 非法 → B NeedsAttention。

## 3. 验收

- [x] Backlog 表增加 TaskKey/DependsOnJson。
- [x] 批准计划写入依赖。
- [x] Worker 派发前检查依赖。
- [x] 依赖未完成时等待。
- [x] 依赖失败时阻断。
- [x] 序列化 taskKey/dependsOn。
