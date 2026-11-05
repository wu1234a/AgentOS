# AOS-002：Event Hub

状态：Completed  
优先级：P0  
依赖：AOS-001

## 1. 目标

建立统一事件接收 API，将外部系统或内部模块发送的事件标准化、验证并保存到
SQL Server。此任务只负责接收和持久化，不负责完整去重和规则分发（分别由
AOS-003、AOS-004 完成）。

## 2. 开始前阅读

除统一开发约定外，必须阅读： - `docs/EVENT-SPECIFICATION.md` -
`docs/DATABASE-ER.md`

## 3. 数据库范围

创建 `Events` 实体和 Migration，字段按数据库设计文档实现。至少包含： -
Id - Source - EventType - SchemaVersion - ExternalEventId - DedupeKey -
Summary - ResourceUrl - PayloadJson - Status - OccurredAtUtc -
ReceivedAtUtc - CorrelationId - ProjectId（可空）

本任务可以先使用普通索引；AOS-003 再完成去重唯一约束和冲突行为。

## 4. API

`POST /api/v1/events`

请求遵循 `EVENT-SPECIFICATION.md` 的 Envelope。

成功响应建议：

``` json
{
  "eventId": "guid",
  "accepted": true,
  "duplicate": false,
  "status": "Received"
}
```

同时提供： - `GET /api/v1/events/{eventId}` -
`GET /api/v1/events`：分页、按 source/type/status/project 过滤。

## 5. 实施要求

1.  创建 Event DTO、Validator、Domain Entity、Repository/Application
    Service。
2.  验证必填字段、长度、日期格式、Payload JSON 格式。
3.  ResourceUrl 只允许 HTTP/HTTPS；不在本任务中主动访问该 URL。
4.  验证 Project Slug 是否存在且启用（若 project 不为空）。
5.  使用 UTC 时间。
6.  为来源身份验证预留 API Client 抽象；开发环境可使用明确标记的
    Development Auth，不允许生产匿名接收。
7.  设置请求体大小限制。
8.  返回统一 ProblemDetails。
9.  写入成功后返回 HTTP 202。
10. 不把完整敏感 Payload 写入日志。

## 6. 安全要求

- 不允许匿名公网调用。
- 不接受命令字段或可执行脚本字段。
- 不主动抓取 ResourceUrl。
- 不将 Payload 内容拼接到 SQL 或 shell 命令。
- API 错误不得泄露连接字符串或堆栈细节。

## 7. 验收标准

- [ ] 合法事件可保存并返回 EventId。
- [ ] 缺少必填字段返回 400。
- [ ] 非法 JSON Payload 返回 400。
- [ ] 未知 Project 返回明确错误。
- [ ] 禁用 Project 不能接收新项目事件。
- [ ] 未授权请求返回 401/403。
- [ ] Event 列表分页和过滤正确。
- [ ] SQL Server Migration 可应用。
- [ ] 测试覆盖成功、失败和权限路径。

## 8. 交付

创建
`docs/reports/AOS-002-DELIVERY-REPORT.md`。不要在本任务中实现去重、规则引擎或
Runner。
