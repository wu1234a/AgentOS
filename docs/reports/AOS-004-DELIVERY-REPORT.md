# AOS-004 交付报告：Rules Engine

**状态：Completed**
**完成时间：2026-11-06**
**任务文件：** `tasks/AOS-004-rules-engine.md`

## 1. 任务目标

实现可配置的事件规则引擎：Event 接收并去重后，按 source / eventType / project 等条件
确定性匹配规则，生成待执行 Task；规则只决定路由，不执行 Agent。

## 2. 实现摘要

### 2.1 数据模型（迁移 `20261106082553_AddRulesEngine`）

| 表 | 说明 |
|---|---|
| `ModelProfiles` | 模型/执行配置，`Code` 唯一，`ConfigurationJson` 有 `ISJSON` 约束，只存 Secret 引用不存密钥 |
| `Skills` | Skill 元数据，`(ProjectId, Code, Version)` 唯一 |
| `Agents` | Agent 角色，项目内 `(ProjectId, Code)` 唯一 + 全局 `Code` 唯一（过滤索引） |
| `AutomationRules` | 规则主表：Name、ProjectId、SourcePattern、EventTypePattern、MatchJson、AgentId、SkillId、ModelProfileId、DispatchJson、Priority、MaxParallel、MaxAttempts、TimeoutSeconds、NotificationPolicy、IsEnabled、Version |
| `Tasks` | 任务：TaskCode 唯一、`LogicalTaskKey` 唯一、RuleId/RuleVersion、AttemptCount、MaxAttempts、TimeoutSeconds、NextRunAtUtc、`rowversion` |

`Tasks` 及其余业务表的外键全部 `Restrict`，历史不可被级联删除。

### 2.2 规则匹配（确定性）

- `SourcePattern` / `EventTypePattern`：`*` 通配、整串匹配、大小写不敏感。
- `ProjectId` 为空 = 全局规则；否则只匹配该项目。
- `MatchJson`：仅声明式条件 `subjectTypes` / `priorities`，未知字段被拒绝（规则不能执行代码）。
- 排序契约：**项目规则优先 → priority 降序 → CreatedAtUtc 升序 → Id 升序**，有单元测试覆盖。

### 2.3 事件 → 任务派发

1. 事件首次持久化后同步派发；规则读取时要求 Agent（及可选 Skill / ModelProfile）处于启用状态。
2. 每条命中规则生成一条 `Queued` 任务，写入 `RuleId` 与 `RuleVersion`。
3. 任务与事件状态（`Processed` / `Ignored`）在**同一事务**提交。
4. 幂等：`LogicalTaskKey = EventId:RuleId:RuleVersion:DispatchKey`，先查已有键，数据库唯一索引兜底；
   并发冲突时重试一次。
5. 事件已入库但派发失败时，重复投递会在去重路径上幂等重试派发，不会永久停留在 `Received`。
6. 无项目归属的事件无法生成任务：跳过规则并记警告，事件状态 `Ignored`。

### 2.4 管理 API

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/v1/rules` | 列表：`page`、`pageSize`、`enabled`、`project` |
| GET | `/api/v1/rules/{id}` | 详情（含 matchJson / dispatchJson） |
| POST | `/api/v1/rules` | 创建，201 |
| PUT | `/api/v1/rules/{id}` | 更新，Version +1 |
| POST | `/api/v1/rules/{id}/enable` | 启用（重新校验引用是否启用） |
| POST | `/api/v1/rules/{id}/disable` | 禁用 |
| POST | `/api/v1/rules/validate` | 只校验不保存，返回 normalized 预览 |

- 全部端点要求 `X-AgentOS-Dev-Key`；错误统一为 `ValidationProblemDetails`。
- 创建/更新时校验：项目存在且启用、Agent/Skill/ModelProfile 存在、项目归属一致、limits 范围、
  `matchJson`/`dispatchJson` 结构与未知字段、`dispatchKey` 不含 `:`。
- 启用时额外要求引用的 Agent / Skill / ModelProfile **已启用**，否则 400 字段错误并保持禁用。

### 2.5 事件响应变化

`POST /api/v1/events` 的 202/200 响应新增 `tasksCreated`，`status` 反映派发结果
（`Processed` / `Ignored`）。重复事件仍返回原 EventId。

### 2.6 前端

- `/rules`：列表（筛选、分页、新建/编辑对话框、启用/禁用），表单直接编辑 `matchJson`/`dispatchJson`
  并把后端字段错误回填到表单项。
- `/rules/:id`：详情页（全部字段 + 两个 JSON 文档 + 启用/禁用）。
- `src/api/rules.js`、路由与侧边栏菜单已接入；`http.js` 支持 `VITE_DEV_API_KEY`
  （开发环境 `X-AgentOS-Dev-Key`）。

## 3. 验收项对照

| 验收标准 | 证据 |
|---|---|
| 创建、读取、修改、启用、禁用规则 | `RulesApiTests`：创建 201、列表/详情、PUT 版本 +1、enable/disable、404 |
| 非法规则 JSON 被拒绝 | `RuleValidatorTests`（未知字段、非对象、dispatchKey 含 `:`）+ `RulesApiTests` 400 |
| 未启用 Agent/Skill/ModelProfile 的规则不能启用 | `RulesApiTests.Enable_is_rejected_when_the_agent_is_disabled`；`RuleValidatorTests.Disabled_*` |
| 一个事件命中零/一/多条规则 | `TaskDispatchServiceTests`（0/1/2 条）+ `SqlRuleDispatchTests` |
| 命中规则生成 Queued 任务 | `TaskDispatchServiceTests.Matching_rule_creates_a_queued_task...` |
| 重复事件不生成重复逻辑任务 | `TaskDispatchServiceTests.Redispatch_...`、`SqlRuleDispatchTests.Dispatch_creates_one_task_per_rule_and_keeps_retries_idempotent` |
| Task 保留 RuleId 和 RuleVersion | 上述 SQL 测试断言 `RuleId` / `RuleVersion = 4` |
| 匹配顺序确定且有测试 | `RuleMatcherTests.Matching_order_...`、`Ordering_is_stable_when_input_is_shuffled` |
| 未匹配事件保留 | 事件状态 `Ignored`，行不删除；`Event_with_no_matching_rule_is_marked_ignored_but_kept` |

## 4. 测试命令与结果

| 检查 | 命令 | 结果 |
|---|---|---|
| 后端构建 | `dotnet build backend/AgentOS.slnx` | ✅ 0 错误 |
| 后端测试（含真实 SQL Server） | 工作区外 `dotnet vstest` / CI `dotnet test` | ✅ **137 通过 / 0 失败**（本机，工作区外运行） |
| 模型一致性 | `dotnet ef migrations has-pending-model-changes` | ✅ No changes |
| 前端 lint | `npm run lint` | ✅ 通过 |
| 前端构建 | `npm run build` | ✅ 成功（Element Plus 包体偏大的既有告警） |
| 迁移 | `dotnet ef database update` | ✅ 已应用到 `AgentOS_Dev` |
| CI（GitHub Actions） | `dotnet build` + `dotnet test`（含 SQL Server service container） | ✅ run#3（功能提交）、run#4（审查修复）均为 SUCCESS |

> 本机 `dotnet test` 仍受工作区沙箱限制（见 AOS-003 报告 §5.2），测试通过工作区外副本 +
> `vstest` 执行；CI（GitHub Actions）用标准 `dotnet test` 全量执行，结果见 Actions。

### 4.1 独立代码审查

第三方（DSH 原生 Codex 规范）审查 commit `2700396`：2 个 P1、4 个 P2、4 个 P3，全部修复或记录，
详见 `docs/reports/AOS-004-CODE-REVIEW.md`。关键修复：

- `sourcePattern` 长度与列宽对齐（65+ 字符返回 400 而不是 500）；
- 前端“编辑”先取详情，避免 PUT 清空 `matchJson`/`dispatchJson`；
- `matchJson` 显式 null 数组归一化，避免 NRE 500；
- `Skills` 索引配置与迁移/快照对齐，`has-pending-model-changes` 干净；
- 新增真实仓储/引用查询的 SQL 测试，并修复了 `JsonElement` 不能直接在 EF 投影中映射的问题；
- PUT 省略 `isEnabled` 时保留当前状态；`FindSkillAsync` 增加 Id 兜底。

## 5. 安全检查

- 规则只能表达声明式条件，不能执行任意代码；未知 JSON 字段被拒绝。
- 规则 CRUD 与事件端点一样要求身份凭证；未认证返回 401。
- 引用校验在保存与启用两处执行，禁用目标无法被规则使用。
- 任务与事件状态同事务提交；外键与唯一索引在数据库层兜底。
- 日志不记录 payload；派发日志只含 EventId、规则数、任务数、状态。
- 未执行生产操作；迁移仅作用于本地 `AgentOS_Dev` 与可丢弃的 `AgentOS_Test`。

## 6. 已知限制

1. 没有 Agent / Skill / ModelProfile 的管理 API 或种子迁移；本地需要 SQL 初始化（见
   `docs/DEVELOPMENT.md` §9.5），管理界面留待后续任务。
2. 规则同步派发；事件量增大后应改为 Outbox + 后台 Worker（AOS-008 之前）。
3. 全局 Skill（`ProjectId` 为 NULL）不强制版本唯一，规则按最新 `CreatedAtUtc` 解析；
   如需严格版本选择，应在后续任务中增加按版本引用。
4. `MaxParallel` 已存储但尚未参与调度限制（AOS-005 的队列实现）。
5. `LogicalTaskKey` 的分发键来自 `dispatchJson.dispatchKey`；同一规则同一版本对同一事件
   只能产生一条任务，多任务扇出需要不同的 dispatchKey。
6. 任务列表 API 与前端任务页面属于 AOS-005/AOS-008。
7. `PUT /api/v1/rules/{id}` 是完整替换；唯一例外是省略 `isEnabled` 时保留当前状态。
   `sourcePattern` 上限 64 字符（与列宽一致），`eventTypePattern` 上限 120。
8. `Tasks.ParentTaskId` 已建模但未建外键/索引，留给 AOS-016 的多阶段任务；AOS-004 不设置该字段。

## 7. 未完成事项

AOS-004 验收范围内无未完成项。以下按任务边界推迟：

- AOS-005：任务队列、领取/租约、重试、`MaxParallel` 并发限制；
- AOS-006/007：Runner 与 Codex Adapter；
- 引用数据（Agent/Skill/ModelProfile）管理 API 与初始化脚本。

## 8. 下一任务建议

AOS-005 Task Queue：

1. `Tasks` 的 `Queued` 领取（原子 UPDATE/OUTPUT，租约 Token 哈希入库）；
2. 心跳续租、超时回收、`RetryWait` → `Queued` 重试；
3. `MaxParallel` 按规则/项目限流；
4. `GET /api/v1/tasks` 列表与详情，供前端与 Runner 使用。

## 9. 变更文件清单

### 新增（后端）

| 路径 | 说明 |
|---|---|
| `backend/AgentOS.Domain/Agents/Agent.cs` | Agent 实体 |
| `backend/AgentOS.Domain/ModelProfiles/ModelProfile.cs` | Model Profile 实体 |
| `backend/AgentOS.Domain/Skills/Skill.cs` | Skill 实体 |
| `backend/AgentOS.Domain/Rules/AutomationRule.cs` | 规则实体 |
| `backend/AgentOS.Domain/Tasks/AutomationTask.cs`、`TaskStatus.cs` | 任务实体与状态 |
| `backend/AgentOS.Application/Rules/*` | 规则契约、匹配器、通配、校验、服务、仓储接口 |
| `backend/AgentOS.Application/Tasks/*` | 派发服务与存储契约 |
| `backend/AgentOS.Infrastructure/Persistence/Configurations/Agent|ModelProfile|Skill|AutomationRule|AutomationTaskConfiguration.cs` | EF 配置与索引 |
| `backend/AgentOS.Infrastructure/Rules/*`、`Tasks/*` | 仓储、引用查询、派发存储 |
| `backend/AgentOS.Infrastructure/Migrations/20261106082553_AddRulesEngine*.cs` | 迁移与快照 |
| `backend/AgentOS.Api/Endpoints/RulesEndpoints.cs` | 规则管理 API |

### 新增（测试与前端）

| 路径 | 说明 |
|---|---|
| `tests/AgentOS.Api.Tests/RulePatternTests.cs`、`RuleMatcherTests.cs`、`RuleValidatorTests.cs` | 匹配与校验单元测试 |
| `tests/AgentOS.Api.Tests/TaskDispatchServiceTests.cs`、`SqlRuleDispatchTests.cs` | 派发幂等与 SQL 集成测试 |
| `tests/AgentOS.Api.Tests/RulesApiTests.cs` | 规则 API 测试 |
| `tests/AgentOS.Api.Tests/Fakes/FakeRuleReferenceLookup.cs`、`FakeAutomationRuleRepository.cs`、`FakeTaskDispatchStore.cs`、`FakeTaskDispatcher.cs` | 测试替身 |
| `frontend/agentos-web/src/views/RulesView.vue`、`RuleDetailView.vue`、`api/rules.js` | 规则页面与 API 封装 |

### 修改

- `backend/AgentOS.Domain/Events/Event.cs`：`Status` / `ProcessedAtUtc` 可写（派发更新）；
- `backend/AgentOS.Application/Events/EventService.cs`、`CreateEventResult.cs`：接入派发与 `tasksCreated`；
- `backend/AgentOS.Infrastructure/Persistence/AgentOSDbContext.cs`：新增 DbSet；
- `backend/AgentOS.Infrastructure/DependencyInjection.cs`、`backend/AgentOS.Api/Program.cs`：注册与路由；
- `backend/AgentOS.Infrastructure/Repositories/EventRepository.cs`：复用 `SqlErrorHelper`；
- `frontend/agentos-web/src/api/http.js`、`router/index.js`、`layouts/AppLayout.vue`、`.env.example`；
- `docs/DEVELOPMENT.md`、`docs/DATABASE-ER.md`、`docs/EVENT-SPECIFICATION.md`。
