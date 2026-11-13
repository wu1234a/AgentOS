# AgentOS 技术文档

- 文档版本：2.0
- 更新时间：2026-11-14
- 适用代码：`58a6cd1`
- 部署形态：Windows 单机 + SQL Server + 本地 Runner

---

## 1. 系统定位

AgentOS 是一个事件驱动、可观测、可恢复的 Agent 自动化平台。它不把业务逻辑塞进一个超级 Agent，而是拆成：

```text
Event Hub
→ Rules Engine
→ Task Queue
→ Local Runner
→ Agent Adapter
→ Validation / Delivery
→ Review / Merge
→ Observability
```

在任务流水线之上，新增了项目级规划层：

```text
Project Plan
→ 多 Agent 提案
→ 交叉评审
→ Chair 综合
→ 人工批准
→ Backlog
→ 依赖派发
```

---

## 2. 总体架构

```text
┌─────────────────────────────────────────────────────────────┐
│                        Vue 3 管理界面                        │
│  Dashboard / Tasks / Planner / Project Plans / Models ...   │
└───────────────────────────┬─────────────────────────────────┘
                            │ HTTPS/HTTP
┌───────────────────────────▼─────────────────────────────────┐
│                     AgentOS.Api (Minimal API)                │
│  Auth Filter / Endpoints / Backlog / ProjectPlanning /       │
│  Review / AutoMerge / Telegram / Hosted Workers              │
└───────────────┬───────────────────────────────┬─────────────┘
                │                               │
        ┌───────▼────────┐              ┌───────▼──────────┐
        │ AgentOS.       │              │ AgentOS.         │
        │ Application    │              │ Infrastructure   │
        │ DTO/Service    │              │ EF Core/SQL      │
        └───────┬────────┘              └───────┬──────────┘
                │                               │
        ┌───────▼────────┐              ┌───────▼──────────┐
        │ AgentOS.Domain │              │ SQL Server       │
        │ Entities       │              │ AgentOS_Prod     │
        └────────────────┘              └──────────────────┘
                │
        ┌───────▼──────────────────────────────────────────┐
        │ AgentOS.AgentAdapters                            │
        │ OpenAI-compatible / Anthropic / Codex CLI       │
        └───────┬──────────────────────────────────────────┘
                │
        ┌───────▼──────────────────────────────────────────┐
        │ AgentOS.Runner                                   │
        │ claim / worktree / execute / validate / deliver  │
        └──────────────────────────────────────────────────┘
```

### 2.1 项目结构

```text
backend/
  AgentOS.Api/             Minimal API、Hosted Workers、Endpoints
  AgentOS.Application/     用例服务、契约、Planner 接口
  AgentOS.Domain/          Event/Task/Backlog/Planning 实体
  AgentOS.Infrastructure/  EF Core、SQL、模型客户端、仓储
  AgentOS.AgentAdapters/   OpenAI 兼容、Anthropic、Codex CLI
  AgentOS.Runner/          本地执行器
  AgentOS.Worker/          后台 Worker
frontend/agentos-web/      Vue 3 + Vite + Element Plus
tests/
  AgentOS.Api.Tests/
```

---

## 3. 关键数据模型

### 3.1 项目与执行

```text
Projects
  Id, Name, Slug, IsEnabled, MaxConcurrentTasks

Events
  Id, ProjectId, Source, EventType, DedupeKey, PayloadJson, Status

Rules
  Id, ProjectId, EventType, MatchJson, AgentId, Enabled

Tasks
  Id, ProjectId, EventId, RuleId, AgentId, Status,
  Priority, LeaseOwner, LeaseExpiresAtUtc, RetryCount

TaskAttempts
  TaskId, AttemptNo, Status, ErrorCode, ErrorMessage, StartedAtUtc

Workers
  WorkerCode, Status, LastSeenAtUtc, Capabilities
```

### 3.2 Backlog

```text
BacklogItems
  Id
  ProjectId
  PlanId                 # 来自哪个立项
  EpicKey                # M1/M2/M3
  TaskKey                # T1/T2/T3
  DependsOnJson          # ["T1"]
  Title
  PayloadJson            # instruction + validationCommands + delivery
  Status                 # Pending/Hold/Dispatched/Completed/...
  EventId
  RepairAttempt
  SourceItemId
  Error
```

状态机：

```text
Pending
  → Dispatched
  → Completed
  → TaskFailed
  → RepairScheduled
  → NeedsAttention
  → Failed
  → Cancelled
  → Hold
```

### 3.3 项目立项

```text
ProjectPlans
  Id, ProjectId, Title, Goal, Constraints
  DeliveryJson
  ModelProfileCode
  Status                   # Queued/Generating/PendingReview/Approved/Rejected/Failed
  ProposalsJson
  CritiquesJson
  FinalPlanJson
  PreviousFinalPlanJson
  Revision
  Feedback
  TokenBudget, TokensUsed, PromptTokens, CompletionTokens
  EstimatedCost, ModelCalls
  RequiresReleaseApproval
  ReleaseApprovedAtUtc, ReleaseApprovedBy
  DecidedAtUtc, DecidedBy
  Error

ProjectPlanContributions
  PlanId, Role, Round, Content, ModelProfileCode

ProjectPlanRevisions
  PlanId, Revision, FinalPlanJson, Feedback,
  ModelProfileCode, TokensUsed, EstimatedCost
```

### 3.4 评审与编码

```text
PullRequestReviewJobs
  Repository, PullRequestNumber, HeadSha, Status, FindingsJson

CodingRuns
  TaskId, Status, Agent, ModelProfile, StartedAtUtc, CompletedAtUtc
```

### 3.5 其他

```text
ModelProfiles
  Code, Provider, Model, ExecutionMode, ConfigurationJson,
  SecretReference, TimeoutSeconds, IsEnabled

Agents
  Code, Role, SystemPrompt, AllowedSkills, Enabled

Skills
  Code, Version, Content, ContentHash, Scope

Approvals
  RequestType, Status, PayloadJson, DecidedAtUtc

UsageRecords
  Provider, Model, PromptTokens, CompletionTokens, EstimatedCost
```

---

## 4. 事件到任务流水线

### 4.1 事件入口

```text
POST /api/v1/events
→ 校验 schema
→ 项目存在且启用
→ 去重（Source + DedupeKey）
→ 持久化 Event
→ 规则匹配
→ 创建 Task
```

### 4.2 任务状态机

```text
Queued
→ Claimed
→ Running
→ Succeeded
→ Failed
→ RetryWait
→ Cancelled
→ Rejected
```

### 4.3 Runner 执行

```text
Runner 启动
→ register
→ claim（带 lease）
→ heartbeat
→ 解析任务 payload
→ 准备 worktree
→ 调用 AgentAdapter
→ 校验
→ 交付
→ complete/fail
```

### 4.4 Worktree 隔离

配置：

```text
Runner:WorktreeRoot = E:\AgentOS.Worktrees
```

流程：

```text
git fetch
→ 从 origin/main 创建临时 worktree
→ 执行 Agent
→ 校验
→ commit/push
→ 清理
```

自动 worktree 创建前会 fast-forward base main，避免基于旧 main 开发。

### 4.5 交付

```text
GitAutoDelivery
→ 检查未提交变更
→ 检查 origin/main..HEAD 提交
→ commit
→ push
→ 创建 PR
```

如果 Agent 已经自行 commit，交付仍会检查分支是否领先 base，不会误报 `delivery-no-changes`。

---

## 5. 编码 Agent 与模型回退

### 5.1 适配器

```text
OpenAiCompatibleAgentAdapter
AnthropicAgentAdapter
CodexCliAgentAdapter
```

### 5.2 回退链

`3derp-coder-model` 的 `configurationJson.fallbacks`：

```json
[
  { "code": "glm-flash" },
  { "code": "glm-flashx" },
  { "code": "3derp-coder-deepseek" }
]
```

实际顺序：

```text
glm-5.3
→ glm-5.3-flash
→ glm-5.3-flashx
→ deepseek-v4-pro
```

触发条件：

```text
401/403/404/408/429/5xx
网络错误
超时
空内容
```

回退时保留消息和工具历史，只重试当前步骤。

---

## 6. 项目立项多 Agent

### 6.1 流程

```text
ProjectPlanWorker
→ 3 个分析角色独立提案
→ 3 个分析角色交叉评审
→ Chair 综合
→ 写入 ProjectPlanRevisions
→ PendingReview
```

角色：

```text
product        产品价值/范围/优先级
architecture   技术方案/依赖/可维护性
cost-risk      成本/周期/风险/性价比
chair          汇总/推荐路线/任务拆解
```

调用上限：

```text
3 proposals + 3 critiques + 1 synthesis = 7 次
```

### 6.2 Chair JSON

```json
{
  "recommendedOption": "...",
  "rationale": "...",
  "constraints": ["..."],
  "roadmap": [
    {"milestone":"M1","goal":"...","exitCriteria":"..."}
  ],
  "risks": [
    {"risk":"...","mitigation":"..."}
  ],
  "acceptanceMetrics": ["..."],
  "tasks": [
    {
      "taskKey": "T1",
      "dependsOn": [],
      "kind": "backend",
      "epic": "M1",
      "title": "...",
      "instruction": "..."
    }
  ]
}
```

校验：

- kind 只能是 backend/frontend/docs/full
- taskKey 唯一
- dependsOn 必须存在
- 不能有环
- 最多 20 条任务

### 6.3 批准

```text
Approve
→ 校验依赖图
→ 解析 delivery
→ 创建 Backlog
→ 写入 PlanId/EpicKey/TaskKey/DependsOn
→ 计划 Approved
```

### 6.4 启动

```text
Plan Approved
→ 可选二次启动审批
→ release
→ Hold → Pending
```

release 支持：

```json
{ "backlogItemIds": ["..."] }
```

```json
{ "epicKeys": ["M1"] }
```

### 6.5 修订

```text
PendingReview/Failed
→ feedback 5-8000 chars
→ Revision + 1
→ 清空 FinalPlan/Approvals
→ Queued
→ 重新跑 board
```

### 6.6 Token/费用

```text
TokenBudget
PromptTokens
CompletionTokens
TokensUsed
EstimatedCost
```

价格：

```text
ModelProfile.configurationJson.inputPricePerMillion
ModelProfile.configurationJson.outputPricePerMillion
```

否则使用：

```text
Planner:ProjectPlanInputPricePerMillion
Planner:ProjectPlanOutputPricePerMillion
```

---

## 7. Backlog 调度

### 7.1 每项目单活动任务

```text
project A: 1 active task max
project B: 1 active task max
```

BacklogWorker 只在一个项目无活动任务时派发。

### 7.2 依赖

```text
Backlog Dependencies
→ 依赖 Completed 才派发
→ 依赖 Failed/Cancelled → NeedsAttention
```

### 7.3 修复

任务失败：

```text
Backlog item TaskFailed
→ repair item（最多 MaxRepairAttempts）
→ repair 分支
→ 重新执行
```

当前 repair 是一级修复，不是无限循环。

---

## 8. Review 与合并

### 8.1 Review

```text
PullRequestReviewJobs
→ diff 拉取
→ 分片（60,000 chars/片，最多 12 片）
→ 模型评审
→ 聚合 findings
→ Critical/Major 阻断
```

### 8.2 合并队列

```text
按 PR number 升序
→ 只推进第一条
→ review 未完成 → 等待
→ mergeable=false → update-branch
→ mergeable=true → merge
→ 成功后本轮结束
```

### 8.3 限制

- 真实文本冲突不会自动改写
- update-branch 只适用于 GitHub 能自动合并的分支
- 合并不是分布式锁；当前单 API 实例

---

## 9. API 摘要

### 事件

```text
POST /api/v1/events
GET  /api/v1/events
GET  /api/v1/events/{id}
```

### 任务

```text
GET  /api/v1/tasks
GET  /api/v1/tasks/{id}
POST /api/v1/tasks/{id}/cancel
```

### Planner

```text
GET  /api/v1/planner/drafts
POST /api/v1/planner/drafts
POST /api/v1/planner/drafts/{id}/approve
POST /api/v1/planner/drafts/{id}/reject
```

### Backlog

```text
GET  /api/v1/backlog
POST /api/v1/backlog
POST /api/v1/backlog/generate
POST /api/v1/backlog/{id}/cancel
```

### Project Plans

```text
POST /api/v1/project-plans
GET  /api/v1/project-plans
GET  /api/v1/project-plans/{id}
POST /api/v1/project-plans/{id}/approve
POST /api/v1/project-plans/{id}/reject
POST /api/v1/project-plans/{id}/approve-release
POST /api/v1/project-plans/{id}/release
POST /api/v1/project-plans/{id}/revise
GET  /api/v1/project-plans/{id}/progress
GET  /api/v1/project-plans/{id}/revisions
```

### 运维

```text
GET /health/live
GET /health/ready
GET /api/v1/workers
GET /api/v1/usage
GET /api/v1/dashboard
```

---

## 10. 认证与安全

### 10.1 管理 API

```text
X-AgentOS-Dev-Key: <Authentication__DevelopmentApiKey>
```

### 10.2 Runner

Runner 使用注册/心跳/租约凭证，任务领取必须带 worker code 和 lease。

### 10.3 审计

```text
X-AgentOS-Actor
→ DecidedBy
→ ReleaseApprovedBy
```

这是审计标识，不是强身份认证。

### 10.4 安全边界

- 默认最小权限
- 只读优先
- 工作区隔离
- 高风险计划二次启动审批
- 不自动执行生产数据库破坏性变更
- 不暴露 API Key 到日志/产物

### 10.5 已知安全缺口

- 没有真实登录/SSO
- Actor header 可伪造
- 没有细粒度 RBAC
- 合并队列不是分布式锁
- 真实文本冲突需要人工

---

## 11. 配置

### 11.1 appsettings

```text
ConnectionStrings:AgentOS
Authentication:DevelopmentApiKey
Runner:WorktreeRoot
Backlog:Enabled/PollSeconds/MaxRepairAttempts
Planner:DefaultModelProfileCode
Planner:ProjectPlanModelProfileCode
Planner:ProjectPlanInputPricePerMillion
Planner:ProjectPlanOutputPricePerMillion
Planner:BacklogFallbackProfileCodes
Review:Provider/Model/BaseUrl/TimeoutSeconds
GitHub:Repository/BaseBranch/...
AutoMerge:...
```

### 11.2 环境变量

```text
Authentication__DevelopmentApiKey
AGENTOS_GITHUB_TOKEN
GitHub__ReviewToken
KIMI_API_KEY
DEEPSEEK_API_KEY
ZHIPU_API_KEY
```

---

## 12. 部署与运维

### 12.1 启动脚本

```text
E:\AgentOS.Prod\start-agentos.ps1
E:\AgentOS.Prod\start-runner.ps1
E:\AgentOS.Prod\watchdog.ps1
E:\AgentOS.Prod\run-hidden.vbs
```

### 12.2 计划任务

```text
AgentOS Startup    登录启动
AgentOS Watchdog   每 5 分钟
```

### 12.3 日志

```text
E:\AgentOS.Prod\logs\api.log
E:\AgentOS.Prod\logs\api.err.log
E:\AgentOS.Prod\logs\watchdog.log
```

### 12.4 数据库迁移

```powershell
dotnet ef database update `
  --project backend\AgentOS.Infrastructure `
  --startup-project backend\AgentOS.Api
```

---

## 13. 测试

```powershell
dotnet build backend\AgentOS.slnx
dotnet test backend\AgentOS.slnx
```

前端：

```powershell
npm --prefix frontend/agentos-web install
npm --prefix frontend/agentos-web run build
```

E2E：

```powershell
npm --prefix frontend/agentos-web run test:e2e
```

当前基线：

```text
309 passed / 64 skipped / 0 failed
```

---

## 14. 已知限制

1. 没有真实登录身份。
2. 真实 Git 文本冲突需人工。
3. 依赖失败不会自动重排。
4. 关键路径没有工期权重。
5. 规划质量依赖模型。
6. 没有多 API 实例合并锁。
7. 未执行真正整机重启验证。
8. 没有生产发布和数据库破坏性变更自动化。

---

## 15. 扩展点

- 新模型：实现 `IAgentAdapter` / 配置 ModelProfile
- 新执行器：新增 Runner 适配器
- 新评审：`IReviewAgent`
- 新规则：Rules API
- 新计划角色：ProjectPlanWorker Analysts
- 新依赖策略：BacklogWorker `CheckDependenciesAsync`
- 新前端页面：Vue View + API client + router

---

## 16. 结论

AgentOS 已经具备：

```text
事件驱动执行
任务级自动交付
项目级多 Agent 规划
人工批准与二次启动审批
版本/审计
依赖顺序执行
模型回退
```

但还没有达到：

```text
真实身份认证
完全自动冲突解决
依赖失败自动重排
完全无人值守生产发布
```

因此当前定位是：

> **高度自动化、可审计、可恢复的本地 Agent 平台，不是完全无人值守的自治研发系统。**
