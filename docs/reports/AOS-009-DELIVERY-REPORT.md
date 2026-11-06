# AOS-009 交付报告：Agent 注册中心与生命周期

**状态：Completed**
**完成时间：2026-11-07**
**任务文件：** `tasks/AOS-009-agent-registry.md`（按 ROADMAP Phase 2 起草并实施）

## 1. 数据模型

- `Agents` 增加 `ArchivedAtUtc`（迁移 `AddAgentLifecycle`）。
- 沿用 `(ProjectId, Code)` / 全局 Code 唯一约束。
- 归档为终态，历史任务/日志/产物保留。

## 2. API

- `GET /api/v1/agents`（projectId / role / includeArchived / limit / offset）
- `GET /api/v1/agents/{id}`
- `POST /api/v1/agents`（201，Code 不可变）
- `PUT /api/v1/agents/{id}`
- `POST /api/v1/agents/{id}/enable|disable|archive`
- 错误：400 校验、404 不存在、409 `agent-code-conflict` / `agent-has-active-tasks` / `agent-archived` / `agent-already-archived`。

## 3. 校验

Code 小写 slug（3–80）；Role 四类；PermissionProfile 两类；DisplayName ≤120；
Description ≤1000；SystemPromptPath 相对路径且禁止 `..`/根路径；Project 与 ModelProfile 必须存在且启用。

## 4. 生命周期

创建 Active → disable/enable 幂等；archive 前检查未终态任务（Queued/Running/RetryWait），
归档后禁止更新/启用/重复归档；禁用 Agent 后 `RuleReferenceLookup` 返回 `IsEnabled=false`，
规则校验拒绝新任务。

## 5. 前端

新增 `/agents` 页面：列表、角色/归档过滤、创建/编辑对话框、启用/禁用/归档；
Vue 3 + JavaScript。

## 6. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **254 通过 / 4 跳过 / 0 失败** |
| AOS-009 SQL 用例 | ✅ 4（创建/列表/更新/重复 Code、非法 Role/权限/路径、禁用与归档守卫、跨项目 Code 复用） |
| 前端 lint / build | ✅ 通过 |
| CI | 见对应 run |

## 7. 安全

- 所有管理端点需要开发 API Key。
- SystemPromptPath 不允许绝对路径或 `..`，避免任意文件引用；后续读取必须限定配置根目录。
- 归档不删除历史数据；禁用不打断运行中任务。

## 8. 已知限制

1. 项目级 Agent 只能通过 API 指定 ProjectId，前端暂只创建全局 Agent（缺项目列表 API）。
2. Agent 版本化、能力标签与模型路由（AOS-010）未实现。
3. 提示词文件只校验路径形状，尚未真正读取和注入 Runner。
4. API 端点契约由 SQL 服务测试与构建覆盖，尚无独立 API 契约测试。

## 9. 变更文件

**新增**: `AgentOS.Domain/Agents`（扩展字段）、`Application/Agents/*`、`Infrastructure/Agents/AgentRepository.cs`、
`Api/Endpoints/AgentsEndpoints.cs`、迁移 `AddAgentLifecycle`、前端 `AgentsView.vue` / `api/agents.js`、
`SqlAgentRegistryTests.cs`。

**修改**: `Agent.cs`、`AgentConfiguration.cs`、`DependencyInjection.cs`、`Program.cs`、router/layout、
`docs/DATABASE-ER.md`、`docs/DEVELOPMENT.md`、任务文件状态。

## 10. 下一任务建议

AOS-010 Model Router / Provider Profiles：ModelProfile CRUD、Provider 配置（不含明文密钥）、
Agent 默认模型解析与执行参数下发。
