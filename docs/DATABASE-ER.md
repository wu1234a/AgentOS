# AgentOS 数据库 ER 设计

数据库：SQL Server  
ORM：Entity Framework Core  
迁移方式：EF Core Code First  
版本：1.0

## 1. 设计原则

1.  Event、Task、Attempt 分表，不能把全部信息塞进 Tasks。
2.  状态变化必须可追踪。
3.  事件去重由数据库唯一约束兜底。
4.  Runner 租约必须持久化并支持超时恢复。
5.  大型日志和产物文件不直接存入数据库；数据库保存元数据和路径。
6.  所有时间统一保存 UTC（`datetimeoffset` 或 UTC
    `datetime2`，项目内必须统一）；前端按用户时区显示。
7.  所有主键建议使用 `uniqueidentifier`，外部可读编号可另设业务 ID。
8.  JSON 字段使用 `nvarchar(max)`，并在适用时增加 `ISJSON` 检查约束。
9.  对并发更新使用 `rowversion`。
10. 生产数据删除和破坏性 Schema 变更必须走审批策略。

## 2. ER 图

``` mermaid
erDiagram
    Projects ||--o{ Agents : contains
    Projects ||--o{ AutomationRules : scopes
    Projects ||--o{ Events : receives
    Projects ||--o{ Tasks : owns
    Projects ||--o{ ApiClients : authorizes
    Agents ||--o{ AutomationRules : selected_by
    Agents ||--o{ Tasks : assigned_to
    ModelProfiles ||--o{ Tasks : uses
    Skills ||--o{ AutomationRules : selected_by
    Skills ||--o{ Tasks : uses
    Events ||--o{ Tasks : creates
    AutomationRules ||--o{ Tasks : creates
    Tasks ||--o{ Tasks : parent_child
    Tasks ||--o{ TaskAttempts : has
    Workers ||--o{ TaskAttempts : executes
    Workers ||--o{ Tasks : leases
    Workers ||--o{ ApiClients : authenticates
    TaskAttempts ||--o{ TaskLogs : emits
    Tasks ||--o{ Artifacts : produces
    TaskAttempts ||--o{ UsageRecords : consumes
    Tasks ||--o{ Approvals : requests
    Approvals ||--o{ Decisions : receives
    Events ||--o{ OutboxMessages : publishes

    Projects {
      uniqueidentifier Id PK
      nvarchar Name
      nvarchar Slug UK
      nvarchar RepositoryUrl
      nvarchar AllowedWorkspaceRoot
      bit IsEnabled
      datetime2 CreatedAtUtc
    }
    Agents {
      uniqueidentifier Id PK
      uniqueidentifier ProjectId FK
      nvarchar Code
      nvarchar DisplayName
      nvarchar Role
      uniqueidentifier DefaultModelProfileId FK
      bit IsEnabled
    }
    ModelProfiles {
      uniqueidentifier Id PK
      nvarchar Code UK
      nvarchar Provider
      nvarchar Model
      nvarchar ExecutionMode
      nvarchar ConfigurationJson
      bit IsEnabled
    }
    Skills {
      uniqueidentifier Id PK
      uniqueidentifier ProjectId FK
      nvarchar Code
      nvarchar Version
      nvarchar RelativePath
      nvarchar ContentHash
      bit IsEnabled
    }
    AutomationRules {
      uniqueidentifier Id PK
      uniqueidentifier ProjectId FK
      uniqueidentifier AgentId FK
      uniqueidentifier SkillId FK
      nvarchar Name
      nvarchar MatchJson
      nvarchar DispatchJson
      bit IsEnabled
      int Version
    }
    Events {
      uniqueidentifier Id PK
      uniqueidentifier ProjectId FK
      nvarchar Source
      nvarchar EventType
      nvarchar DedupeKey
      nvarchar PayloadJson
      nvarchar Status
      datetime2 OccurredAtUtc
      datetime2 ReceivedAtUtc
    }
    Tasks {
      uniqueidentifier Id PK
      uniqueidentifier EventId FK
      uniqueidentifier ProjectId FK
      uniqueidentifier RuleId FK
      uniqueidentifier AgentId FK
      uniqueidentifier SkillId FK
      uniqueidentifier ModelProfileId FK
      uniqueidentifier ParentTaskId FK
      uniqueidentifier LeaseOwnerWorkerId FK
      nvarchar Status
      nvarchar Priority
      nvarchar LogicalTaskKey UK
      int AttemptCount
      int MaxAttempts
      datetime2 LeaseExpiresAtUtc
      datetime2 NextRunAtUtc
      rowversion RowVersion
    }
    Workers {
      uniqueidentifier Id PK
      nvarchar WorkerCode UK
      nvarchar HostName
      nvarchar Platform
      nvarchar Version
      nvarchar CapabilitiesJson
      nvarchar Status
      datetime2 LastHeartbeatAtUtc
    }
    TaskAttempts {
      uniqueidentifier Id PK
      uniqueidentifier TaskId FK
      uniqueidentifier WorkerId FK
      int AttemptNumber
      nvarchar Status
      nvarchar ExternalSessionId
      int ExitCode
      datetime2 StartedAtUtc
      datetime2 FinishedAtUtc
    }
    TaskLogs {
      bigint Id PK
      uniqueidentifier TaskAttemptId FK
      int Sequence
      nvarchar Level
      nvarchar Message
      datetime2 CreatedAtUtc
    }
    Artifacts {
      uniqueidentifier Id PK
      uniqueidentifier TaskId FK
      uniqueidentifier TaskAttemptId FK
      nvarchar Name
      nvarchar MediaType
      nvarchar StoragePath
      nvarchar Sha256
      bigint SizeBytes
    }
    Approvals {
      uniqueidentifier Id PK
      uniqueidentifier TaskId FK
      nvarchar ApprovalType
      nvarchar Status
      nvarchar Summary
      datetime2 ExpiresAtUtc
    }
    Decisions {
      uniqueidentifier Id PK
      uniqueidentifier ApprovalId FK
      nvarchar Decision
      nvarchar Actor
      nvarchar Comment
      datetime2 DecidedAtUtc
    }
    UsageRecords {
      uniqueidentifier Id PK
      uniqueidentifier TaskAttemptId FK
      nvarchar Provider
      nvarchar Model
      bigint InputTokens
      bigint OutputTokens
      decimal EstimatedCost
      datetime2 RecordedAtUtc
    }
    ApiClients {
      uniqueidentifier Id PK
      uniqueidentifier ProjectId FK
      uniqueidentifier WorkerId FK
      nvarchar ClientCode UK
      nvarchar ClientType
      nvarchar SecretHash
      nvarchar ScopesJson
      bit IsEnabled
      datetime2 ExpiresAtUtc
    }
    OutboxMessages {
      uniqueidentifier Id PK
      uniqueidentifier EventId FK
      nvarchar MessageType
      nvarchar PayloadJson
      nvarchar Status
      int AttemptCount
      datetime2 CreatedAtUtc
      datetime2 ProcessedAtUtc
    }
```

## 3. 核心表定义

以下为逻辑字段基线。具体 SQL 类型、长度和命名由 EF Core Entity
Configuration 实现。

### 3.1 Projects

用途：注册 AgentOS 管理的项目。 - `Id` uniqueidentifier PK - `Name`
nvarchar(120) - `Slug` nvarchar(80)，唯一 - `Description`
nvarchar(1000)，可空 - `RepositoryUrl` nvarchar(500)，可空 -
`DefaultBranch` nvarchar(120)，可空 - `AllowedWorkspaceRoot`
nvarchar(500) - `ConfigurationJson` nvarchar(max)，默认 `{}`，必须为合法
JSON - `IsEnabled` bit - `CreatedAtUtc`、`UpdatedAtUtc`

约束：Slug 唯一；禁用项目不能创建新任务。

### 3.2 ModelProfiles

用途：模型/执行配置，不存储明文 API Key。 - `Id` - `Code` 唯一 -
`Provider`：例如 `codex-cli`、未来的 `openai-api`、`kimi-api` -
`Model`：模型标识 - `ExecutionMode`：`cli` / `api` -
`ConfigurationJson`：非敏感参数 -
`SecretReference`：指向环境变量或密钥管理器的名称，不保存密钥值 -
`TimeoutSeconds` - `MaxCostPerTask` 可空 - `IsEnabled` - 时间字段

### 3.3 Agents

用途：定义 Agent 角色。 - `Id` - `ProjectId` 可空（全局 Agent） -
`Code` - `DisplayName` - `Role` - `Description` -
`DefaultModelProfileId` - `SystemPromptPath` 可空 -
`PermissionProfile` - `IsEnabled` - 时间字段

约束：全局和项目级 Agent 的 Code
唯一性应通过明确索引策略实现；不要仅依赖前端检查。

### 3.4 Skills

用途：记录 Skill 文件元数据，正文保存在 Git 管理的 Markdown 文件中。 -
`Id` - `ProjectId` 可空 - `Code` - `Version` - `RelativePath` -
`ContentHash` - `Description` - `IsEnabled` - `CreatedAtUtc`

唯一约束建议：`ProjectId + Code + Version`。

### 3.5 AutomationRules

用途：事件路由规则。 - `Id` - `ProjectId` 可空 - `Name` -
`SourcePattern` - `EventTypePattern` - `MatchJson` - `AgentId` -
`SkillId` 可空 - `DispatchJson` - `Priority` - `MaxParallel` -
`MaxAttempts` - `TimeoutSeconds` - `NotificationPolicy` - `IsEnabled` -
`Version` - `CreatedAtUtc`、`UpdatedAtUtc`

`MatchJson` 与 `DispatchJson` 必须通过应用层 schema
验证。规则执行时必须记录命中的 RuleId 和 Version。

### 3.6 Events

用途：统一事件账本。 - `Id` - `ProjectId` 可空 - `Source` -
`EventType` - `SchemaVersion` - `ExternalEventId` 可空 - `DedupeKey` -
`Summary` - `ResourceUrl` 可空 - `PayloadJson` -
`Status`：`Received`、`Processed`、`Ignored`、`Rejected`、`Failed` -
`OccurredAtUtc` - `ReceivedAtUtc` - `ProcessedAtUtc` 可空 -
`CorrelationId` 可空

索引： - `Source + DedupeKey` 唯一（按产品约定处理 NULL；建议 DedupeKey
必填）。 - `ReceivedAtUtc`。 - `ProjectId + EventType + ReceivedAtUtc`。

### 3.7 Tasks

用途：任务主表。 - `Id` - `TaskCode` 可读业务编号，唯一 - `EventId`
可空（手动任务） - `ProjectId` - `RuleId` 可空 - `RuleVersion` 可空 -
`AgentId` - `SkillId` 可空 - `ModelProfileId` 可空 - `ParentTaskId`
可空 - `LeaseOwnerWorkerId` 可空 - `Title` - `Description` -
`InputJson` - `ResultJson` 可空 - `Status` - `Priority` -
`AttemptCount` - `MaxAttempts` - `TimeoutSeconds` - `NextRunAtUtc` -
`LeaseTokenHash` 可空（不得存明文租约 Token） - `LeaseExpiresAtUtc`
可空 - `LastHeartbeatAtUtc` 可空 - `StartedAtUtc` 可空 -
`CompletedAtUtc` 可空 - `ErrorCode`、`ErrorMessage` 可空 -
`CreatedAtUtc`、`UpdatedAtUtc` - `RowVersion`

索引： - `Status + Priority + NextRunAtUtc`（队列领取）。 -
`ProjectId + CreatedAtUtc`。 - `EventId`。 -
`LeaseExpiresAtUtc`（过期租约扫描）。 - `ParentTaskId`。

### 3.8 Workers

用途：本地 Runner 注册信息。 - `Id` - `WorkerCode` 唯一 -
`DisplayName` - `HostName` - `Platform` - `Architecture` - `Version` -
`CapabilitiesJson` - `Status`：Online、Offline、Disabled -
`LastHeartbeatAtUtc` - `RegisteredAtUtc` - `UpdatedAtUtc`

认证凭证应存储哈希或交给外部 Secret Store，不能存明文。

### 3.9 TaskAttempts

用途：每次执行尝试的独立记录。 - `Id` - `TaskId` - `WorkerId` -
`AttemptNumber` - `Status` - `ExternalSessionId` 可空 - `StartedAtUtc` -
`FinishedAtUtc` 可空 - `ExitCode` 可空 - `StdoutArtifactId` 可空（若以
Artifact 形式保存） - `StderrArtifactId` 可空 -
`ErrorCode`、`ErrorMessage` 可空 - `DurationMs` 可空

唯一约束：`TaskId + AttemptNumber`。

### 3.10 TaskLogs

用途：结构化执行日志。 - `Id` bigint identity - `TaskAttemptId` -
`Sequence` - `Level` - `Message` - `PropertiesJson` 可空 -
`CreatedAtUtc`

唯一约束：`TaskAttemptId + Sequence`。日志内容必须脱敏。

### 3.11 Artifacts

用途：任务产物元数据。 - `Id` - `TaskId` - `TaskAttemptId` 可空 -
`Name` - `MediaType` - `StorageProvider`：初期 `local` - `StoragePath` -
`Sha256` - `SizeBytes` - `CreatedAtUtc`

文件存储在配置的 Artifact
根目录，数据库只保存路径、哈希和元数据。下载必须经过授权
API，不得直接暴露任意文件路径。

### 3.12 Approvals / Decisions

AOS-018 以后启用。 -
Approvals：TaskId、ApprovalType、Status、Summary、RiskLevel、RequestedAtUtc、ExpiresAtUtc。 -
Decisions：ApprovalId、Decision、ActorId、ActorDisplayName、Comment、DecisionPayloadJson、DecidedAtUtc。

审批回调必须验证签名/身份、过期时间和幂等键。

### 3.13 UsageRecords

用途：记录模型调用成本。 - `Id` - `TaskAttemptId` - `Provider` -
`Model` - `InputTokens` - `OutputTokens` - `CachedTokens` 可空 -
`EstimatedCost` - `Currency` - `IsEstimate` - `RecordedAtUtc`

成本字段允许为空或标记估算值，不能伪称所有 CLI 都能提供精确 Token 成本。

### 3.14 OutboxMessages

用途：可靠发布任务状态变化和事件。 - `Id` - `EventId` 可空 -
`MessageType` - `PayloadJson` - `Status` - `AttemptCount` -
`NextAttemptAtUtc` - `CreatedAtUtc` - `ProcessedAtUtc` 可空 -
`LastError` 可空

Outbox 与业务记录在同一数据库事务中写入，再由 Worker 投递。

## 4. 关系与删除策略

- Projects 对 Events、Tasks、Agents 使用
  Restrict，不能因删除项目而级联删除历史记录。
- Tasks 对 Attempts、Logs、Artifacts 使用 Restrict 或受控归档策略。
- AutomationRules、Agents、Skills 默认软禁用，不物理删除。
- Events 和 Tasks 保留审计历史。
- 对日志和大型产物设置保留期限，但清理必须由专门的保留策略任务执行，并记录清理结果。
- 生产环境禁止级联删除任务历史。

## 5. 并发和队列约束

- 任务领取必须在数据库事务中完成。
- SQL Server
  可使用适当的行锁提示或存储过程实现原子领取；具体实现必须通过真实 SQL
  Server 并发测试验证。
- 使用 `rowversion` 防止并发覆盖。
- 租约 Token 仅返回给对应 Runner，数据库只保存哈希。
- Runner 更新任务状态时必须验证 Worker、Attempt、租约和当前状态。
- 同一个任务同一时间只能存在一个有效租约。

## 6. Migration 规则

1.  每个 AOS 任务独立提交 Migration。
2.  Migration 只能增量演进。
3.  不得在启动时自动删除或重置数据库。
4.  破坏性变更采用
    Expand-and-Contract：先新增兼容结构，迁移数据，再在单独审批窗口清理旧结构。
5.  每个 Migration 提供升级测试和必要的恢复说明。
6.  SQL Server 集成测试必须使用真实 SQL Server，而不是仅用 EF InMemory
    替代。

## 7. 第一阶段实际创建范围

AOS-001～AOS-008 至少需要创建： - Projects - ModelProfiles - Agents -
Skills - AutomationRules - Events - Tasks - Workers - TaskAttempts -
TaskLogs - Artifacts - OutboxMessages

Approvals、Decisions、UsageRecords
可以在数据库设计中预留，但应由对应后续里程碑实现，避免第一阶段过度开发。

### 3.15 ApiClients

用途：为事件来源、Runner 等机器身份提供 API 凭证。 - `Id` - `ClientCode`
唯一 - `ClientType`：`EventSource`、`Runner`、`Service` - `ProjectId`
可空 - `WorkerId` 可空 - `SecretHash`：只保存凭证哈希，不保存明文 -
`ScopesJson`：允许的 API Scope - `IsEnabled` - `ExpiresAtUtc` 可空 -
`LastUsedAtUtc` 可空 - `CreatedAtUtc`、`UpdatedAtUtc`

约束： - ClientType 为 Runner 时必须关联
Worker（注册引导阶段可先使用一次性注册凭证）。 - 每个 Client 只能访问其
Scopes 和项目范围。 - 密钥只在创建/轮换时显示一次。 - 认证失败不能泄露
Client 是否存在。
