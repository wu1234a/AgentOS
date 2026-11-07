# AOS-027：定时任务和日历式调度（间隔调度 MVP）

状态：Completed
优先级：P1
依赖：AOS-002 Event Hub、AOS-004 Rules、AOS-005 Runner
来源：Kimi Planner 草案 `01a1b77e-b281-7d17-8be7-11cdad5ebbe8`（已批准）

## 0. 修订

Kimi 草案含完整 cron/日历、删除、补偿、去重、并发。本阶段先交付：

1. `ScheduledJobs` 表 + 间隔调度（最小 60 秒）。
2. 后台 Worker 扫描到期任务，通过 EventService 发布事件。
3. CRUD/启停 API。
4. 按计划时间推进、错过合并为一次补偿、`schedule:<id>:<scheduledFor>` 去重。
5. 不做 cron 解析、前端日历、多实例选主。

## 1. 范围

- 表字段：Name、EventType、PayloadJson、IntervalSeconds、FirstRunAtUtc、NextRunAtUtc、Enabled、LastTriggeredAtUtc。
- API `/api/v1/scheduled-jobs`：
  - GET/POST、GET/PATCH/DELETE、enable/disable。
- 创建：首次时间默认 now+interval；过去时间钳制为 now。
- Worker 每 15 秒执行；发布 source=`scheduler`，payload 含 scheduleId/scheduledFor/triggeredAt/compensated/missedCount。
- 发布成功/重复后推进到未来首个周期；EventService 校验失败则停用任务。
- 迁移 `AddScheduledJobs`。

## 2. 不在范围

- cron/每日/每周/时区/节假日。
- 前端日历/管理页。
- 多实例选主/分布式锁。
- 手动补跑/历史回放。
- 直接调用 Rules/Runner（只走 EventService）。

## 3. 安全

- 调度 CRUD 使用开发 Key。
- EventType/payload 仍走 EventService 校验，不绕过规则。
- payload/名称长度限制；最小间隔 60 秒防风暴。
- 日志不记录敏感 payload。

## 4. 验收

- [x] 到期任务发布一次事件并推进 NextRun。
- [x] 重复扫描不重复推进。
- [x] CRUD/启停。
- [x] 后端全量测试、CI 通过。
