# AgentOS 日常运行：Dashboard + 日志

适用：个人单用户运行模式
通知策略：不接 Telegram，以 Dashboard、健康检查和日志为准

## 1. 入口

```text
https://choosing-yesterday-couples-lows.trycloudflare.com/
```

Quick Tunnel 重启后地址会变化，Watchdog 会自动更新 GitHub Webhook；新地址记录在：

```text
E:\AgentOS.Prod\logs\tunnel-url.txt
```

本地入口：

```text
http://127.0.0.1:5099/
```

## 2. 每日检查

### Dashboard

打开首页，确认：

- 统计卡片有数据
- 没有明显 401/500

### Workers

`/workers`

- Worker 是否 Online
- 是否有运行任务
- 心跳是否正常

### Pipelines

`/pipelines`

- 最近任务是否正常
- 是否有 Failed/Blocked 任务
- 点开任务查看阶段时间线

### Usage

`/usage`

- 调用次数、Token、费用是否符合预期
- 是否出现 `MissingPrice`

### Projects

`/projects`

- 项目是否 Enabled
- 配额是否合理

## 3. 健康检查

```powershell
Invoke-RestMethod http://127.0.0.1:5099/health/live
Invoke-RestMethod http://127.0.0.1:5099/health/ready
Invoke-RestMethod http://127.0.0.1:5099/api/v1/version
```

预期：

```text
live  = Healthy
ready = Healthy
version = 200
```

## 4. 日志位置

```text
E:\AgentOS.Prod\logs\
├─ api.log              API 请求与业务日志
├─ api.err.log          API 标准错误
├─ watchdog.log         守护任务日志
├─ quality-reconcile.log 质量门禁对账日志
├─ auto-merge.log       Auto-merge 脚本日志
└─ tunnel-url.txt       当前公网地址
```

tunnel 进程日志在：

```text
E:\TempTunnel.log
E:\TempTunnelErr.log
```

## 5. 实时看日志

API：

```powershell
Get-Content E:\AgentOS.Prod\logs\api.log -Wait -Tail 50
```

错误：

```powershell
Get-Content E:\AgentOS.Prod\logs\api.err.log -Wait -Tail 50
```

只看 GitHub Webhook / 审查：

```powershell
Get-Content E:\AgentOS.Prod\logs\api.log -Wait |
  Select-String "GitHub webhook|PR review|quality"
```

只看错误：

```powershell
Get-Content E:\AgentOS.Prod\logs\api.log -Tail 200 |
  Select-String '"@l":"Error"|"@l":"Warning"'
```

## 6. 常见问题

### API 没反应

```powershell
Get-CimInstance Win32_Process |
  Where-Object { $_.CommandLine -like '*E:\AgentOS.Prod\AgentOS.Api.dll*' }
```

没有进程：

```powershell
& E:\AgentOS.Prod\start-agentos.ps1
```

### 公网地址打不开

```powershell
Get-Process cloudflared
Get-Content E:\AgentOS.Prod\logs\tunnel-url.txt
```

没有进程时，Watchdog 最多 5 分钟内会重启；也可手动：

```powershell
& E:\AgentOS.Prod\watchdog.ps1
```

### GitHub Webhook 失败

1. 打开 GitHub → Settings → Webhooks → Recent Deliveries
2. 检查 URL 是否与 `tunnel-url.txt` 一致
3. 失败的事件可点 Redeliver

### 质量门禁一直 pending

```powershell
& E:\AgentOS.Prod\reconcile-quality-gates.ps1
```

或打开 PR 重新触发 CI。

### 任务失败/死信

打开：

```text
/workers
/pipelines
```

查看任务状态和阶段详情；必要时通过死信 API 重试。

## 7. 每周检查

- 备份是否生成：`E:\AgentOS.Backups`
- 磁盘空间
- GitHub Token 是否临近过期
- Codex CLI 路径是否仍有效
- PR 审查/质量门禁是否正常
- 是否有大量 Failed/Blocked 任务

## 8. 备份与恢复

手动备份：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File E:\AgentOS.Prod\backup-agentos.ps1
```

恢复演练建议在 `AgentOS_Test` 上进行，不要直接覆盖 `AgentOS_Prod`。

## 9. 不接通知时的原则

- 每天打开一次 Dashboard
- 出现异常时先看 `api.log` 和 `/workers`
- 依赖 Watchdog 自动恢复 API/Tunnel
- 重要操作前手动备份
