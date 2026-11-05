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

重复事件：返回已存在的 Event ID，并标识
`duplicate: true`。重复事件不能再次创建同一规则版本下的同一逻辑任务。

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

如果相同 `source + dedupeKey` 再次到达： - 若内容一致：返回已有
Event。 - 若内容不同：记录冲突并返回明确响应，不得静默覆盖原事件。

## 6. 事件处理和任务创建

1.  验证身份和 Envelope。
2.  验证项目是否存在且启用。
3.  在事务中写入 Event。
4.  执行规则匹配，或写入 Outbox 交给后台处理。
5.  对每条命中的规则生成 Task。
6.  Task 保存 RuleId 和 RuleVersion，确保之后可以追溯当时使用的规则。
7.  更新 Event 处理状态。
8.  发布状态变化。

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
