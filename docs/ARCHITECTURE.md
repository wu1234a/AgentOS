# AgentOS 技术架构文档

版本：1.0  
状态：第一阶段架构基线

## 1. 架构概述

AgentOS 采用控制平面与执行平面分离的架构。

- **控制平面（Control
  Plane）**：API、事件接收、规则匹配、任务持久化、审批、配置和监控。
- **执行平面（Execution Plane）**：本地 Runner、工作区管理器、Agent
  Adapter、Codex CLI 等实际执行组件。

控制平面不直接启动本地开发电脑上的进程。Runner
主动向控制平面请求任务，因此本地电脑不需要暴露公网入站端口。

## 2. 总体架构

``` mermaid
flowchart TB
    subgraph Sources["事件来源"]
      GH["GitHub Webhook"]
      FEI["飞书事件（后续）"]
      TIMER["定时事件（后续）"]
      APP["项目内部事件"]
      UI["管理后台手动任务"]
    end

    subgraph Control["AgentOS 控制平面"]
      API["ASP.NET Core Minimal API"]
      AUTH["API Client / 身份验证"]
      HUB["Event Hub"]
      DEDUP["事件去重"]
      RULES["Rules Engine"]
      QUEUE[("SQL Server Task Queue")]
      WORKER["AgentOS.Worker"]
      REALTIME["SignalR"]
      API --> AUTH --> HUB --> DEDUP --> RULES --> QUEUE
      WORKER <--> QUEUE
      API <--> REALTIME
    end

    subgraph Execution["本地执行平面"]
      RUNNER["AgentOS.Runner"]
      WORKSPACE["Workspace Manager"]
      ADAPTER["Agent Adapter"]
      CODEX["Codex CLI"]
      RUNNER --> WORKSPACE --> ADAPTER --> CODEX
    end

    GH --> API
    FEI --> API
    TIMER --> API
    APP --> API
    UI --> API
    QUEUE <-->|HTTPS Pull / Lease / Heartbeat| RUNNER
    RUNNER --> API
    API --> UI
```

## 3. 组件职责

### 3.1 AgentOS.Api

职责： - 提供版本化 REST API。 - 验证 API Client 身份和权限范围。 -
接收事件、查询任务、管理规则和项目。 - 为 Runner
提供领取、心跳、日志和完成回传接口。 - 通过 SignalR
推送管理后台状态变化。

不得： - 在 HTTP 请求线程中执行长时间 Agent 进程。 -
直接执行未经授权的任意 shell 命令。 - 将生产密钥返回给前端或 Runner
日志。

### 3.2 AgentOS.Domain

包含： - Event、Task、TaskAttempt、Worker、Rule、Project 等领域模型。 -
状态枚举和状态转换规则。 - 领域约束和不变量。

不得依赖 EF Core、HTTP、Vue 或具体模型 SDK。

### 3.3 AgentOS.Application

包含： - 用例服务、DTO、验证器、接口抽象。 - Event Ingestion、Rule
Evaluation、Task Claim、Heartbeat、Complete 等应用流程。 -
事务边界和权限检查。

### 3.4 AgentOS.Infrastructure

包含： - EF Core DbContext 和 Migration。 - SQL Server 持久化。 -
文件系统 Artifact Store 的第一版实现。 - Outbox 持久化。 -
配置读取和日志实现。

### 3.5 AgentOS.Worker

服务端后台工作： - 清理过期租约。 - 将可重试任务重新排队。 - 处理
Outbox。 - 处理过期事件和保留策略。 - 第一阶段不执行 Codex；Codex 由本地
Runner 执行。

### 3.6 AgentOS.Runner

本地常驻执行程序： - 启动时注册 Worker。 - 主动向 API 领取任务。 -
定时发送心跳并续租。 - 在允许的项目根目录中创建工作区。 - 调用 Agent
Adapter。 - 回传状态、日志和产物元数据。 - 断线后按退避策略重连。

### 3.7 AgentOS.AgentAdapters

统一模型/Agent 适配接口。第一阶段只实现 Codex CLI
Adapter。未来可以增加其他 CLI 或 API 模型适配器。

## 4. 技术栈与工程约束

### 后端

- ASP.NET Core Minimal API、.NET 10。
- EF Core、SQL Server。
- FluentValidation（如项目采用，保持统一；也可使用内置验证，不能重复引入多套验证体系）。
- Serilog 结构化日志。
- SignalR。
- Quartz.NET 在定时任务里程碑启用，不纳入 AOS-001 基础范围。

### 前端

- Vue 3 + JavaScript + Vite。
- Pinia、Vue Router、Axios。
- Element Plus。
- 只使用 JavaScript：`.js`、`.vue`。
- 禁止 `.ts`、`.tsx` 和 `<script lang="ts">`。
- API 请求统一封装在 `src/api/`，页面组件不直接散落 Axios 调用。
- API 响应、错误和加载状态统一处理。

### 数据库

- SQL Server。
- EF Core Code First。
- 所有结构变化通过 Migration。
- 不允许在运行时代码中自动删除数据或重置数据库。
- 开发测试数据库与未来生产数据库必须分离。

## 5. 任务执行主流程

``` mermaid
sequenceDiagram
    participant S as Event Source
    participant A as AgentOS API
    participant D as SQL Server
    participant R as Local Runner
    participant C as Codex Adapter
    participant U as User

    S->>A: POST /events
    A->>D: 保存 Event（幂等）
    A->>D: 匹配 Rule 并创建 Task
    A-->>S: 202 Accepted
    R->>A: 请求领取任务
    A->>D: 原子领取并创建 Attempt
    A-->>R: Task + Skill + 执行限制
    R->>C: 执行任务
    C-->>R: stdout/stderr/exit code
    R->>A: 日志、心跳、完成状态
    A->>D: 保存执行记录
    A-->>U: 管理后台状态更新
```

## 6. 事件、任务和执行尝试的区别

- Event：外部或内部发生的一件事，例如 GitHub 出现一个新 Issue。
- Task：规则引擎决定系统应该完成的工作，例如分析该 Issue。
- TaskAttempt：某个 Runner 实际执行该 Task 的一次尝试。

一个 Event 可以创建零个、一个或多个 Task。一个 Task 可以有多次
Attempt。重试不能覆盖之前的 Attempt 记录。

## 7. 任务状态机

第一阶段至少支持：

- Pending：已创建，尚未进入队列。
- Queued：等待 Runner 领取。
- Running：正在执行。
- RetryWait：等待重试。
- Succeeded：执行成功。
- Failed：不可重试或超过最大尝试次数。
- Cancelled：已取消。
- AwaitingApproval：等待审批（后续阶段启用）。
- Rejected：审批拒绝（后续阶段启用）。

``` mermaid
stateDiagram-v2
    [*] --> Pending
    Pending --> Queued
    Queued --> Running
    Running --> Succeeded
    Running --> RetryWait
    RetryWait --> Queued
    Running --> Failed
    Pending --> Cancelled
    Queued --> Cancelled
    Running --> Cancelled
    Running --> AwaitingApproval
    AwaitingApproval --> Queued: Approved
    AwaitingApproval --> Rejected: Rejected
```

状态转换必须由服务端领域逻辑校验，不能由前端直接任意修改。

## 8. Runner 领取和租约

Runner 采用主动拉取模式： - 注册后轮询 Claim API。 -
建议使用长轮询或短轮询；初版可采用 5～15 秒轮询，后续根据任务量优化。 -
Claim 成功后获得 `leaseToken`，该 Token 只用于当前任务租约操作。 -
Runner 按配置间隔发送 Heartbeat。 - 任务未完成时续租。 -
任务完成、失败或取消时释放租约。 - Runner 失联后，租约过期，由服务端
Worker 将任务转入 RetryWait 或重新排队。 -
所有领取操作必须是数据库原子操作，不能先 SELECT 再无锁 UPDATE。

建议默认： - LeaseDuration：90 秒。 - HeartbeatInterval：20 秒。 -
TaskTimeout：按规则配置，默认 30 分钟。 - MaxAttempts：默认 2 次。
这些数值是初始配置，必须可配置，不得写死在业务逻辑中。

## 9. Rules Engine

规则负责： - 匹配 `source`、`type`、`project`、优先级等条件。 - 选择
Agent、Skill、ModelProfile 和项目。 - 设置并发、超时、最大尝试次数。 -
指定执行工作区权限。 - 指定通知策略和后续审批策略。

规则不负责执行进程。规则匹配结果生成 Task，由 Runner 执行。

规则存储策略： - 数据库 `AutomationRules` 是运行时权威数据源。 - JSON
文件用于样例、初始化导入和版本管理。 -
每条规则有版本、启用状态和更新时间。 -
规则修改需要审计记录（完整审计中心在后续阶段扩展）。

## 10. Agent、Model Profile 和 Skill

### Agent

定义角色和职责，例如 `planner`、`coder`、`reviewer`、`qa`。Agent
不直接绑定某个厂商的模型名称，而是引用 ModelProfile。

### ModelProfile

定义 provider、model、参数、执行方式、成本上限和超时。第一阶段可以只配置
Codex CLI Profile，不要求接入多家 API。

### Skill

Skill 是可版本化 SOP。建议目录：

``` text
skills/
├── common/
│   ├── task-execution/
│   │   └── SKILL.md
│   └── report-writing/
│       └── SKILL.md
├── software-development/
└── projects/
    ├── 3derp/
    └── emperor-simulator/
```

Skill 必须说明： - 适用场景。 - 输入。 - 输出。 - 执行步骤。 -
权限限制。 - 验收标准。 - 失败处理。 - 相关参考文件。

不要把 API Key 或密码写进 Skill。

## 11. 工作区隔离

每个写入型任务必须使用独立工作区。建议工作区根目录：

``` text
E:\AgentOS-Workspaces\
├── 3derp\
├── emperor-simulator\
└── agentos\
```

要求： - 工作区根目录必须在项目白名单中。 - 通过 Git Worktree
或隔离副本创建任务目录。 - 不允许 Agent 直接写入用户日常主工作目录。 -
任务结束后根据保留策略归档或清理。 -
清理前检查是否存在未提交改动；不得静默丢弃。 -
同一分支/同一资源不能被两个写入任务同时占用。

## 12. API 设计（第一阶段）

统一前缀：`/api/v1`

### Health

- `GET /health/live`
- `GET /health/ready`

### Events

- `POST /events`
- `GET /events/{eventId}`

### Tasks

- `GET /tasks`
- `GET /tasks/{taskId}`
- `POST /tasks/{taskId}/cancel`（受权限控制）

### Runner

- `POST /runners/register`
- `POST /runners/heartbeat`
- `POST /runners/claim`
- `POST /runners/tasks/{taskId}/start`
- `POST /runners/tasks/{taskId}/logs`
- `POST /runners/tasks/{taskId}/complete`
- `POST /runners/tasks/{taskId}/fail`

### Rules（基础管理）

- `GET /rules`
- `POST /rules`
- `PUT /rules/{ruleId}`
- `POST /rules/{ruleId}/enable`
- `POST /rules/{ruleId}/disable`

### Projects

- `GET /projects`
- `GET /projects/{projectId}`

API 具体请求和响应以各 AOS
任务文件为准。所有写接口都要验证身份、权限和输入。

## 13. 前端页面规划

第一阶段： - Dashboard：服务状态、待执行任务、运行任务、失败任务。 -
Events：事件列表和事件详情。 -
Tasks：任务列表、任务详情、执行尝试和日志。 -
Runners：在线状态、最后心跳。 - Rules：规则列表和基础编辑。 -
Projects：项目注册信息。

后续： - Agent Center。 - Skill Center。 - Approval Center。 - Decision
Center。 - Cost Center。 - Automation Map。

## 14. 错误处理和可恢复性

- Event 接收与 Task 创建必须使用事务或 Outbox 保证一致性。
- 重复 Event 不重复生成相同任务。
- Runner 请求超时可以安全重试。
- Claim API 必须幂等或通过租约避免重复领取。
- 任务重试保留旧 Attempt。
- 超过最大尝试次数进入 Failed，不允许无限重试。
- 服务端重启后从数据库恢复任务。
- Runner 断线后重新注册/续接，不能伪造旧租约。
- 对不可恢复错误进入人工处理状态。

## 15. 测试策略

- Domain 单元测试：状态转换、规则条件、租约判断。
- Application 单元测试：事件处理、规则匹配、任务生成。
- Infrastructure 集成测试：SQL Server、唯一约束、并发领取。
- API 集成测试：身份验证、输入校验、错误响应。
- Runner 测试：断线、重连、心跳、任务取消。
- Adapter 测试：进程参数、超时、退出码、日志脱敏。
- E2E：从事件接收到 Runner 完成任务的完整闭环。

## 16. 架构决策

- 初期使用 SQL Server 持久化队列，不立即引入 Redis。
- 初期不引入 LangGraph；确定性流程使用 .NET 应用服务和状态机。
- 初期不引入 Dify；AgentOS 自己管理事件、任务、Runner 和权限。
- 初期不引入向量数据库；先使用项目文档和 Skill 文件。
- 初期使用本地 Runner 主动拉取，不开放本地公网入站端口。
- 先使用 Codex CLI Adapter，后续再增加其他 Provider。
