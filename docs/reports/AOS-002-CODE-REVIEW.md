# AOS-002 独立代码审查记录

- **审查对象：** AOS-002 Event Hub 的暂存变更（36 个文件）
- **审查方式：** `dsh_code_review` —— Codex 审查 rubric 的 DSH 原生实现，独立子 Agent，只读
- **审查结论：** 无 Blocker；1 High、5 Medium、4 Low
- **复审：** 修复后在提权终端重新构建/测试

## 1. 审查发现与处理

| 级别 | 问题 | 位置 | 处理 |
|---|---|---|---|
| High | 无法解析的请求体（非法 JSON、空 body）被异常处理映射为 500，违反“无效事件 400” | `Program.cs` | `UseExceptionHandler` 增加 `StatusCodeSelector`：`BadHttpRequestException` 返回其状态码（400）；新增非法 JSON、空 body 测试 |
| Medium | `page` 无上界，`(page-1)*pageSize` 溢出后 SQL OFFSET 为负，返回 500 | `EventsEndpoints.cs`、`EventRepository.cs` | `page` 限制 1..1,000,000；仓储用 `long skip` 并防溢出；新增 `page=30000000` 测试 |
| Medium | `occurredAtUtc` 无时区会被按服务器本地时区静默换算 | `CreateEventValidator.cs`、`CreateEventRequest.cs` | DTO 改为字符串；Validator 要求显式 `Z` 或偏移；Service 复用同一解析；新增无时区测试 |
| Medium | SQL 集成测试缺环境变量时静默通过，CI 实际从未执行 | `SqlEventRepositoryTests.cs` | 改用 `Xunit.SkippableFact` + `Skip.If` 显式跳过；CI SQL service container 作为后续补强并在报告中列明 |
| Medium | 开发 API Key 明文提交到 Git | `appsettings.Development.json` | 删除字面量；改为 User Secrets/环境变量；未配置时 fail-closed；文档更新 |
| Medium | 缺少 AOS-002 交付报告和 API/认证/Projects 初始化文档 | `docs/` | 新增本报告、交付报告、DEVELOPMENT 第 8 节 |
| Low | 请求体上限 256 KB 硬编码两处 | `Program.cs`、`EventsEndpoints.cs` | 抽为 `Events:MaxRequestBodyBytes`，Kestrel 与端点过滤器共用 |
| Low | `status` 过滤接受数字和逗号组合 | `EventsEndpoints.cs` | 改为 `Enum.GetNames<EventStatus>()` 精确匹配；新增 `status=1`、`status=Processed,Received` 测试 |
| Low | Events 缺少 DATABASE-ER 的 `ProcessedAtUtc` | 实体/配置/迁移 | 增加可空列与 `AddProcessedAtUtc` 迁移；DTO 返回 `processedAtUtc` |
| Low | 非 Development 的 fail-closed 路径没有测试 | `UnconfiguredApiClientAuthenticator` | 新增单元测试断言其恒返回 `null`；生产端点测试因测试宿主配置限制改用该单元测试覆盖 |

## 2. 复审结果

| 检查 | 结果 |
|---|---|
| `dotnet build backend/AgentOS.slnx` | 0 警告 / 0 错误 |
| `dotnet test backend/AgentOS.slnx` | 33 通过 / 0 失败 / 1 跳过（SQL 集成测试默认跳过） |
| SQL 集成测试（`AGENTOS_SQL_TEST_CONNECTION`） | 1 通过 / 0 失败 |
| `/health/live`、`/health/ready` | 200；SQL Healthy |
| POST / GET 详情 / GET 列表 | 202 / 200 / 200 |
| 未授权 / 未知项目 / 禁用项目 | 401 / 400 / 400 |
| 非法 JSON / 空 body / 413 / 超大 page | 400 / 400 / 413 / 400 |

## 3. 残余项

- CI 尚未加入 SQL Server service container；SQL 集成测试在 CI 中显式跳过。建议在 AOS-003/005
  前补上 service container，避免迁移/并发去重测试只依赖本地环境。
- `Projects` 仍是最小表，项目管理 API 与 ER 其余字段留给后续任务。
- 本次审查使用 `dsh_code_review`（Codex rubric），不是 Codex CLI；如需真正的 Codex/Claude
  二次复核，可在新会话使用 `dsh-plugin-subagent-bridge` 的 `codex_dispatch`/`claude_dispatch`。
