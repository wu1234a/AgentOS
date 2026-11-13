# AgentOS 使用教程

- 文档版本：2.0
- 更新时间：2026-11-14
- 适用代码：`58a6cd1`
- 适用环境：Windows 本地单机部署

> 结论先说：AgentOS 当前已经实现**任务级和立项级高度自动化**，但还不是完全无人值守。
> 方案批准、启动审批、高风险操作和真实身份认证仍需要人参与。本教程按当前能力编写。

---

## 1. AgentOS 是什么

AgentOS 是一个个人可控、多项目共用的 Agent 自动化平台。它负责：

```text
接收事件
→ 按规则创建任务
→ 本地 Runner 领取任务
→ 在隔离 Git worktree 中执行
→ 调用编码 Agent
→ 构建/测试校验
→ commit/push/PR
→ 自动评审
→ 按队列合并
→ 失败自动修复
```

在它之上还有一条项目级链路：

```text
产品目标
→ 多 Agent 立项
→ 3 份提案 + 交叉评审 + Chair 综合
→ 人工批准路线
→ 生成带 PlanId/Epic/TaskKey 的 Backlog
→ 按依赖顺序执行
→ 版本快照与操作审计
```

---

## 2. 当前自动化边界

### 已自动化

- 事件接收、规则匹配、任务创建
- 本地 Runner 任务领取、租约、心跳、重试
- Git worktree 隔离
- 编码 Agent 执行
- 构建/测试校验
- 自动 commit / push / PR
- 自动评审（大 diff 分片）
- 自动合并队列（按 PR 编号串行）
- 过期分支 update-branch
- 模型 429/网络失败自动回退
- Backlog 自动派发、完成回写、失败修复
- 项目立项多 Agent 提案/评审/综合
- 计划批准、启动二次审批、Epic/子集启动
- Backlog 任务依赖执行
- 版本快照和操作者审计

### 仍需人工

- 目标和约束的输入
- 项目方案批准
- 启动执行二次批准
- 高风险/生产操作审批
- 真实 Git 文本冲突解决
- 依赖失败后的重排
- 账号登录与权限控制

### 尚不能称为“完全无人值守”的原因

1. `X-AgentOS-Actor` 只是审计标识，不是真实登录身份。
2. 真实文本冲突仍需要人工。
3. 依赖失败只会阻断下游，不会自动重排。
4. 没有真正整机重启验证记录。
5. 规划质量依赖模型，仍需人审。

---

## 3. 环境准备

### 3.1 必需软件

| 软件 | 用途 |
|---|---|
| .NET 10 SDK | 后端、Runner、测试 |
| Node.js 20+ | Vue 前端 |
| SQL Server | AgentOS 持久化 |
| Git | 仓库和 worktree |
| PowerShell 7 | 启动和运维脚本 |
| GitHub Token | PR、评审、合并 |
| 模型 API Key | Planner/Coder/Reviewer |

### 3.2 关键目录

```text
E:\Projects\AgentOS               # 源码
E:\AgentOS.Prod                   # 生产运行目录
E:\AgentOS.Worktrees              # Runner 隔离 worktree
E:\AgentOS.Prod\logs              # 日志
E:\AgentOS.Prod\wwwroot           # 前端静态文件
```

### 3.3 关键环境变量

不要把真实值写入代码或文档。

```text
Authentication__DevelopmentApiKey   # 管理 API 的 Dev Key
AGENTOS_GITHUB_TOKEN                # GitHub 经典 Token
GitHub__ReviewToken                 # 评审/合并 Token，通常与上面一致
KIMI_API_KEY                        # Planner
DEEPSEEK_API_KEY                    # 回退/项目规划
ZHIPU_API_KEY                       # GLM
OPENAI_API_KEY                      # 可选
DASHSCOPE_API_KEY                   # 可选
```

检查是否存在（不打印值）：

```powershell
$names = @(
  'Authentication__DevelopmentApiKey',
  'AGENTOS_GITHUB_TOKEN',
  'KIMI_API_KEY',
  'DEEPSEEK_API_KEY',
  'ZHIPU_API_KEY'
)
foreach ($name in $names) {
  $value = [Environment]::GetEnvironmentVariable($name, 'User')
  "{0}: {1}" -f $name, [bool]$value
}
```

### 3.4 数据库

默认生产库：

```text
Server=lpc:localhost
Database=AgentOS_Prod
Trusted_Connection=True
```

检查：

```powershell
sqlcmd -S localhost -d AgentOS_Prod -E -Q "SELECT TOP 1 MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC"
```

---

## 4. 启动和停止

### 4.1 手动启动 API

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File E:\AgentOS.Prod\start-agentos.ps1
```

### 4.2 手动启动 Runner

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File E:\AgentOS.Prod\start-runner.ps1
```

### 4.3 Watchdog

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File E:\AgentOS.Prod\watchdog.ps1
```

Watchdog 会：

- 同步 3dERP main
- 启动 API
- 启动 Runner
- 启动 cloudflared
- 同步 GitHub webhook

### 4.4 Windows 计划任务

```text
AgentOS Startup   登录时启动
AgentOS Watchdog  每 5 分钟保活
```

查看状态：

```powershell
Get-ScheduledTask -TaskName 'AgentOS Startup','AgentOS Watchdog' |
  Select-Object TaskName,State

Get-ScheduledTaskInfo -TaskName 'AgentOS Startup' |
  Select-Object LastRunTime,LastTaskResult
```

手动模拟登录启动：

```powershell
Start-ScheduledTask -TaskName 'AgentOS Startup'
Start-Sleep -Seconds 20
```

### 4.5 健康检查

```powershell
Invoke-RestMethod http://127.0.0.1:5099/health/live
Invoke-RestMethod http://127.0.0.1:5099/health/ready
```

### 4.6 停止

不要直接杀用户/编辑器启动的 Godot 进程；AgentOS 自身可以按 PID 停：

```powershell
Get-CimInstance Win32_Process |
  Where-Object {
    $_.CommandLine -like '*AgentOS.Prod\AgentOS.Api.dll*' -or
    $_.CommandLine -like '*AgentOS.Runner.dll*'
  } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
```

---

## 5. 管理界面

打开：

```text
http://127.0.0.1:5099
```

主要页面：

| 页面 | 用途 |
|---|---|
| Dashboard | 总览 |
| Events | 事件列表 |
| Tasks | 任务、执行状态、日志 |
| Planner | 单任务规划草案 |
| Project Plans | 多 Agent 立项、路线图、批准、启动 |
| Approvals | 人工审批 |
| Coding Runs | 编码执行记录 |
| Skills | 可版本化 SOP |
| Models | 模型 Profile |
| Workers | Runner 心跳 |
| Pipelines | 任务流水线 |
| Projects | 项目、并发配额 |
| Usage | Token/成本统计 |
| Agents | Agent 注册 |
| Runners | Runner 注册 |
| Rules | 事件→任务规则 |

---

## 6. 使用方式一：单任务 Planner

适合“一个明确的小功能”。

### 6.1 UI 操作

1. 打开 `Planner`
2. 点击“生成草案”
3. 填写标题、目标、上下文
4. 选择模型 Profile
5. 生成后人工查看
6. 批准 → 自动创建任务
7. Runner 领取并执行
8. 在 Tasks / Coding Runs 查看结果
9. 在 GitHub 查看 PR

### 6.2 API 创建草案

```powershell
$key = [Environment]::GetEnvironmentVariable('Authentication__DevelopmentApiKey','User')
$headers = @{ 'X-AgentOS-Dev-Key' = $key; 'Content-Type' = 'application/json' }

$body = @{
  title = '库存页面增加导出'
  goal  = '为库存余额页面增加 CSV 导出，复用现有权限过滤。'
  context = 'Vue 3 + JavaScript；不要引入 TypeScript。'
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
  -Uri 'http://127.0.0.1:5099/api/v1/planner/drafts' `
  -Headers $headers -Body $body
```

批准：

```powershell
Invoke-RestMethod -Method Post `
  -Uri 'http://127.0.0.1:5099/api/v1/planner/drafts/<draftId>/approve' `
  -Headers $headers
```

---

## 7. 使用方式二：多 Agent 项目立项

适合“从目标到路线图”。

### 7.1 UI 流程

1. 打开 `Project Plans`
2. 点击“新建立项”
3. 选择项目
4. 填写标题和目标
5. 填写约束：预算、周期、团队、技术栈、不可改动项
6. 设置 Token 预算
7. 选择是否需要二次启动审批
8. 填写交付仓库和分支
9. 点击“开始规划”

规划过程：

```text
Queued
→ Generating（3 提案 + 3 评审 + 1 综合）
→ PendingReview
```

### 7.2 查看详情

详情抽屉包含：

- 推荐方案
- 推荐理由
- M1/M2/M3 路线图
- 任务拆解
- 风险
- 验收指标
- 3 份提案
- 3 份交叉评审
- 版本历史
- 执行进度
- Backlog 明细
- 关键路径

### 7.3 批准路线

`PendingReview` 时：

```text
批准并生成 Backlog
→ 计划 Approved
→ 生成带 PlanId / EpicKey / TaskKey / DependsOn 的 Backlog
```

拒绝：

```text
拒绝 → 计划 Rejected
```

### 7.4 人工修订

`PendingReview` 或 `Failed` 时：

1. 输入至少 5 个字符的人工意见
2. 点击“按意见重新规划”
3. 计划重新 Queued
4. Revision + 1
5. 上一版最终方案保留

### 7.5 启动执行

如果计划启用了二次启动审批：

1. 点击“批准启动”
2. 再点击“启动选中 / 启动全部 / 启动 Epic”

如果未启用：

```text
直接启动选中 / 启动全部 / 启动 Epic
```

### 7.6 Backlog 依赖

```text
T1 完成前，T2 不派发
T1 Failed/Cancelled → T2 NeedsAttention
```

### 7.7 版本历史

版本历史记录：

- 版本号
- 模型
- Tokens
- 费用估算
- 人工意见
- 创建时间
- 查看 JSON
- 对比当前任务差异

---

## 8. 使用方式三：直接 Backlog / 事件

### 8.1 创建 Backlog

```powershell
$body = @{
  projectId = '<projectId>'
  title = '修复库存分页边界'
  instruction = '修复 pageSize 边界并补充测试。'
  validationCommands = @('dotnet build 3dERP.slnx','dotnet test 3dERP.slnx --no-build')
  priority = 'normal'
  delivery = @{
    repository = 'wu1234a/3dERP'
    baseBranch = 'main'
    remote = 'origin'
    tokenEnvironmentVariable = 'AGENTOS_GITHUB_TOKEN'
    requirePullRequest = $true
  }
} | ConvertTo-Json -Depth 6

Invoke-RestMethod -Method Post `
  -Uri 'http://127.0.0.1:5099/api/v1/backlog' `
  -Headers $headers -Body $body
```

### 8.2 查看 Backlog

```powershell
Invoke-RestMethod `
  -Uri 'http://127.0.0.1:5099/api/v1/backlog?status=Pending' `
  -Headers $headers
```

### 8.3 取消 Backlog

```powershell
Invoke-RestMethod -Method Post `
  -Uri 'http://127.0.0.1:5099/api/v1/backlog/<itemId>/cancel' `
  -Headers $headers
```

### 8.4 提交原始事件

```powershell
$event = @{
  schemaVersion = '1.0'
  source = 'manual'
  eventType = 'task.requested'
  externalEventId = 'manual-001'
  project = '3derp'
  subject = @{ type = 'manual'; id = 'manual-001' }
  summary = '手动任务'
  occurredAtUtc = (Get-Date).ToUniversalTime().ToString('O')
  dedupeKey = 'manual-001'
  priority = 'normal'
  payload = @{
    instruction = '做一个最小改动并验证。'
    validationCommands = @('dotnet build 3dERP.slnx')
  }
} | ConvertTo-Json -Depth 8

Invoke-RestMethod -Method Post `
  -Uri 'http://127.0.0.1:5099/api/v1/events' `
  -Headers $headers -Body $event
```

---

## 9. 模型配置

### 9.1 Model Profile

后台 `Models` 页面维护：

```text
code
provider
model
executionMode = api
baseUrl
secretReference
timeoutSeconds
configurationJson
```

### 9.2 回退链

编码 Agent 支持：

```text
glm-5.3
→ glm-5.3-flash
→ glm-5.3-flashx
→ deepseek-v4-pro
```

回退触发：

- HTTP 401/403/404/408/429/5xx
- 网络错误
- 超时
- 空内容

### 9.3 项目规划模型

```text
ProjectPlanModelProfileCode = deepseek-reviewer
```

Kimi 仍可用于普通 Planner，失败时用 DeepSeek。

---

## 10. 监控和日常运维

### 10.1 看 Runner

```powershell
Invoke-RestMethod `
  -Uri 'http://127.0.0.1:5099/api/v1/workers' `
  -Headers $headers
```

期望：

```text
3derp-local  Online
```

### 10.2 看活动任务

```powershell
$tasks = Invoke-RestMethod `
  -Uri 'http://127.0.0.1:5099/api/v1/tasks?page=1&pageSize=20' `
  -Headers $headers

$tasks.items | Where-Object {
  $_.status -in @('Queued','Running','RetryWait','AwaitingApproval','Blocked')
} | Select-Object id,status,title
```

### 10.3 看日志

```text
E:\AgentOS.Prod\logs\api.log
E:\AgentOS.Prod\logs\api.err.log
E:\AgentOS.Prod\logs\watchdog.log
```

Runner 日志在 Runner 运行目录，文件名类似：

```text
runner-*.log
runner-*.err.log
```

### 10.4 看 PR

```powershell
$token = [Environment]::GetEnvironmentVariable('AGENTOS_GITHUB_TOKEN','User')
$gh = @{
  Authorization = "Bearer $token"
  'User-Agent' = 'AgentOS/1.0'
  Accept = 'application/vnd.github+json'
}

Invoke-RestMethod `
  -Uri 'https://api.github.com/repos/wu1234a/3dERP/pulls?state=open' `
  -Headers $gh
```

---

## 11. 常见问题

### Runner Offline

1. 检查进程
2. 检查 `start-runner.ps1`
3. 检查 Runner 日志
4. 检查网络到 API
5. 检查 Worker 是否被禁用

### 任务一直 Running

检查：

```text
Task lease 是否过期
Runner 是否还活着
worktree 是否被锁
Agent 适配器是否超时
```

### 模型 429

确认回退链：

```text
Models → 3derp-coder-model → fallbacks
```

### PR 不合并

检查：

```text
review 是否 Succeeded
mergeable 是否 true
base 是否 main
分支是否 aos/
是否有 Critical/Major
二次启动审批是否通过（计划场景）
```

### Backlog 不派发

检查：

```text
项目是否有活动任务
依赖是否 Completed
项目是否启用
Backlog 是否 Hold
规则是否启用
```

### API 启动失败

```powershell
Get-Content E:\AgentOS.Prod\logs\api.err.log -Tail 80
Get-Content E:\AgentOS.Prod\logs\api.log -Tail 80
```

---

## 12. 安全注意事项

- 不要提交真实 Token/Key。
- 不要把 `config.js` 中的 Dev Key 发到公开仓库。
- `X-AgentOS-Actor` 只是审计字段，不是身份认证。
- 生产数据库迁移必须有人批准。
- 二次启动审批默认开启，不建议关闭。
- 高风险路径应继续保留人工审批。

---

## 13. 从零跑通的最小示例

```text
1. 启动 API 和 Runner
2. 打开 Project Plans
3. 新建一个“增加只读报表”的立项
4. 等待 PendingReview
5. 查看提案和路线图
6. 批准
7. 批准启动
8. 选择 M1 启动
9. 在 Tasks 看执行
10. 在 GitHub 看 PR
11. 在版本历史/进度看结果
```

---

## 14. 当前版本结论

```text
任务级自动化     已可用
项目立项自动化   已可用
依赖顺序执行     已可用
审计/版本        已可用
完全无人值守     尚未达到
```

如果你要把它用于真实项目，建议保持：

```text
人给目标
人批准方案
人批准高风险启动
AgentOS 执行、验证、开 PR、评审、合并
人做最终验收
```
