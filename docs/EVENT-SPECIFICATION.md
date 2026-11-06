# AgentOS 统一事件协议

版本：1.0

## 1. 目的

所有事件来源都必须转换为统一的 Event Envelope。Event Hub
不应依赖某个来源平台的原始字段结构。

## 2. 标准事件结构

``` json
{
  "schemaVersion": "1.0",
  "source": "3derp",
  "eventType": "development.task.created",
  "externalEventId": "source-event-123",
  "project": "3derp",
  "subject": {
    "type": "development_task",
    "id": "DEV-008"
  },
  "summary": "DEV-008 已创建，需要分析开发范围",
  "resourceUrl": null,
  "occurredAtUtc": "2026-11-05T14:00:00Z",
  "dedupeKey": "3derp:DEV-008:created",
  "priority": "normal",
  "correlationId": "corr-optional",
  "payload": {
    "description": "任务详细说明"
  }
}
```

字段规则： - `schemaVersion`：必填，协议版本。 -
`source`：必填，来源标识，使用小写 kebab-case 或小写 slug。 -
`eventType`：必填，使用 `domain.entity.action` 格式。 -
`externalEventId`：来源系统的事件 ID，可空。 - `project`：项目
slug，可空（全局事件）。 - `subject`：事件对象类型和 ID。 -
`summary`：简短摘要，建议不超过 500 字符。 -
`resourceUrl`：回到原系统的链接，可空；必须是允许的 HTTP/HTTPS URL。 -
`occurredAtUtc`：发生时间，必须带时区。 -
`dedupeKey`：幂等去重键，必填。 -
`priority`：low、normal、high、urgent。 - `correlationId`：跨服务追踪
ID。 - `payload`：来源相关数据，必须为 JSON 对象，不得包含密码、API Key
或 Cookie。

## 3. 事件命名

建议： - `development.task.created` - `development.task.updated` -
`github.issue.opened` - `github.pull_request.opened` -
`github.pull_request.synchronize` - `github.check.failed` -
`runner.registered` - `runner.offline` - `task.completed` -
`task.failed` - `erp.order.import.failed` - `erp.print_job.failed` -
`schedule.daily.report`

事件类型必须稳定，不要将中文描述或临时 UI 名称作为事件类型。

## 4. 接收 API

`POST /api/v1/events`

成功首次接收：HTTP 202，返回 Event ID 和接收状态。

重复事件：HTTP 200，返回已存在的 Event ID，并标识
`duplicate: true`。重复事件不能再次创建同一规则版本下的同一逻辑任务。

去重键冲突（相同 `source + dedupeKey`、不同 `payload`）：HTTP 409，响应体为
`ProblemDetails`，扩展字段 `code = event.dedupe.conflict`，并包含
`existingEventId` 与 `attemptedEventId`。原事件保持不变。

无效事件：HTTP 400，返回字段级错误。

未授权：HTTP 401/403。

超出大小限制：HTTP 413。

建议初始限制： - Envelope 最大 256 KB。 - Summary 最大 500 字符。 -
ResourceUrl 最大 2048 字符。 - Payload 必须是 JSON object。
以上均可配置。

## 5. 去重策略

去重键建议由来源系统构造： -
GitHub：`github:{repo}:{eventType}:{deliveryId}`。 -
业务对象：`{source}:{project}:{subjectType}:{subjectId}:{action}:{revision}`。 -
定时任务：`schedule:{jobCode}:{scheduledAtUtc}`。

数据库必须有唯一约束作为最终防线。应用层先查再插入不能替代唯一约束。

### 5.1 数据库约束（AOS-003）

- `Events` 上存在唯一索引 `UX_Events_Source_DedupeKey (Source, DedupeKey)`。
- 比较语义遵循数据库排序规则：默认安装下 `Source`、`DedupeKey` 按**不区分大小写**
  比较，因此 `3derp` 与 `3DERP` 视为同一来源；某些字符（如 emoji）在 Windows 排序规则中
  没有权重，比较时会被忽略。需要区分大小写或按字节比较时必须显式修改列排序规则，
  不能只改应用代码。
- 迁移前已存在的历史数据：不删除任何行。空去重键按**存储长度**判断（避免 emoji 被排序规则
  当作“可忽略”而误判为空），改写为 `legacy-{EventId}`；同一 `Source + DedupeKey` 的多行中，
  最早一行保留原键，其余按以下规则改名：
  - 原键长度（UTF-16 单元）不超过 176：`{原键}~legacy-dup-{EventId}`；
  - 原键更长（塞不下后缀）：`legacy-dup-{EventId}`；
  - 改名后仍与既有字面量键冲突：`legacy-dup-{EventId}-{8 位随机十六进制}`。

  改写后旧键不会自动恢复。

### 5.2 PayloadHash 策略（固定，v1）

`PayloadHash` = `payload` 规范 JSON 的 SHA-256（小写十六进制，64 字符）：

- 对象属性按名称**序数（ordinal）**升序排列；同名属性保留最后一次出现的值。
- 数组顺序保持不变。
- 数字保留接收时的字面形式（`1` 与 `1.0` 不同）。
- 不输出任何格式化空白。
- 字符串使用 UTF-8 最小转义（仅转义引号、反斜杠和控制字符），中文与 emoji 保持原样。

实现位置：`EventPayloadHasher`，策略常量 `agentos.payload-canonical-json/v1`。
`Events.PayloadJson` 对新事件保存的就是该规范形式，哈希与存储内容一一对应。
比较范围只包含 `payload`；`summary`、`occurredAtUtc` 等信封字段不参与冲突判断，
以便来源系统安全重试。修改上述任一规则都会改变已存储哈希，必须升级策略版本并回填。

### 5.3 响应约定

| 场景 | 状态码 | 响应 |
|---|---|---|
| 首次接收 | 202 Accepted | `{ eventId, accepted: true, duplicate: false, status }` |
| 相同 `source + dedupeKey` 且内容一致 | 200 OK | `{ eventId: 原 EventId, accepted: false, duplicate: true, status }` |
| 相同 `source + dedupeKey` 但内容不同 | 409 Conflict | `ProblemDetails`，`code: event.dedupe.conflict`，含 `existingEventId`、`attemptedEventId` |

- 冲突时原事件不被覆盖、不被删除，新内容不落库。
- 重复与冲突日志按“去重键 + 类别（duplicate / conflict）”分别采样：每个键 60 秒窗口内
  首条必记，其余每 100 条记一条，窗口切换时报告被抑制的条数。冲突走独立计数，因此即使
  同一键先经历大量内容一致的重试，每次窗口内的第一条 409 仍会留下安全日志。
- 采样器最多跟踪 4096 个键，超出时先淘汰过期窗口、再淘汰最旧窗口至一半容量，避免重试
  风暴导致内存无界增长或每次请求都做全量清理。
- 历史行（迁移前写入）的 `PayloadHash` 为 NULL，首次命中去重时按 v1 策略计算并回填；
  回填失败不影响响应，下次重试会再次计算。

## 6. 事件处理和任务创建

1.  验证身份和 Envelope。
2.  验证项目是否存在且启用。
3.  写入 Event（AOS-003 去重）。
4.  执行规则匹配（AOS-004，同步进行；规则引擎是数据库驱动的确定性匹配）。
5.  对每条命中的规则生成 Task（`Queued`）。
6.  Task 保存 RuleId 和 RuleVersion，确保之后可以追溯当时使用的规则。
7.  更新 Event 处理状态：命中并路由到项目 → `Processed`；未命中或无法路由 → `Ignored`。
    事件永远保留，不会因为“没有规则”被删除。
8.  （Outbox / 实时推送）留给后续任务。

实现补充（AOS-004）：

- 任务与事件状态在同一数据库事务中提交；任一步失败都不会留下“有任务但事件仍是
  Received”或反之的半成品。
- 事件已持久化但派发失败时，重复投递会在去重路径上重试派发；`LogicalTaskKey` 唯一索引
  保证不会重复创建任务。
- 规则匹配顺序固定：项目规则优先 → priority 降序 → 创建时间升序 → Id 升序。

如果规则引擎暂时不可用，事件应保持可重试状态，不能丢弃。

## 7. 安全要求

- Webhook 必须验证来源身份。
- 支持 HMAC 签名或每个来源独立 API Client。
- 验证签名时使用原始请求 Body，使用恒定时间比较。
- 防止重放：校验时间窗口，并结合外部 Delivery ID 去重。
- 限流和请求体大小限制。
- Payload 视为不可信输入。
- 不允许事件内容直接成为 shell 命令。
- 不允许未经净化的事件文本覆盖系统 Prompt 或 Skill 指令。
- ResourceUrl 不得自动抓取任意内网地址，避免 SSRF。

## 8. 事件与任务幂等

Event 幂等不等于 Task 幂等。应增加逻辑任务唯一标识，例如：
`EventId + RuleId + RuleVersion + DispatchKey`。

相同 Event
命中同一规则时不得生成重复任务；规则版本变更是否允许重新生成任务必须由明确的
Replay 操作决定。

## 9. 失败与重试

- 临时数据库或网络故障：重试。
- 验证失败：Rejected，不重试。
- 未匹配规则：Ignored 或 ProcessedNoMatch，保留事件。
- Agent 执行失败：由 Task 重试策略处理。
- 超过最大尝试次数：Failed / Dead-letter，等待人工处理。
- 重放历史事件必须有独立权限和审计记录。
