# AgentOS 个人生产运行（第一阶段）

状态：已完成基础运行化
更新：2026-11-09

## 1. 目录与数据库

```text
应用目录：E:\AgentOS.Prod
日志：    E:\AgentOS.Prod\logs
备份：    E:\AgentOS.Backups
数据库：  AgentOS_Prod
端口：    127.0.0.1:5099
```

数据库已执行全部 EF 迁移，并创建项目：

```text
Name: AgentOS
Slug: agentos
```

## 2. 进程启动

启动脚本：

```text
E:\AgentOS.Prod\start-agentos.ps1
```

当前使用“个人单用户模式”：

```text
ASPNETCORE_ENVIRONMENT=Development
```

原因：Production 环境下尚未实现 API Client 管理界面/认证；Development 模式使用随机强 API Key，便于个人管理接口访问。

启动方式：

- 登录时通过 Startup 快捷方式自动运行
- 或手动执行：

```powershell
powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File E:\AgentOS.Prod\start-agentos.ps1
```

## 3. 配置与密钥

非敏感配置：

```text
E:\AgentOS.Prod\appsettings.Production.json
```

敏感配置存放于当前 Windows 用户环境变量：

```text
GitHub__WebhookSecret
GitHub__ReviewToken
Codex__ExecutablePath
Authentication__DevelopmentApiKey
```

查看 API Key：

```powershell
[Environment]::GetEnvironmentVariable('Authentication__DevelopmentApiKey','User')
```

不要把该值提交到仓库或发送到公开渠道。

## 4. 自动任务

### 开机/登录启动

Startup 快捷方式：

```text
%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\AgentOS.lnk
```

### 每日备份

任务计划：

```text
AgentOS Daily Backup
每天 03:00
```

备份脚本：

```text
E:\AgentOS.Prod\backup-agentos.ps1
```

保留 14 天。

手动备份：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File E:\AgentOS.Prod\backup-agentos.ps1
```

### 5 分钟守护

任务计划：

```text
AgentOS Watchdog
每 5 分钟
```

职责：

- API 未运行则启动
- cloudflared 未运行则启动
- 公网 URL 变化时自动更新 GitHub Webhook

脚本：

```text
E:\AgentOS.Prod\watchdog.ps1
```

## 5. 公网入口

当前使用 cloudflared Quick Tunnel：

```text
https://<随机>.trycloudflare.com
```

当前 URL 记录：

```text
E:\AgentOS.Prod\logs\tunnel-url.txt
```

Watchdog 会自动把新 URL 写回 GitHub Webhook。

⚠️ Quick Tunnel 是临时地址，重启后 URL 会变化；适合个人联调，不是长期稳定域名。

正式稳定入口下一步应改为：

- Cloudflare Named Tunnel + 自有域名，或
- Tailscale Funnel，或
- 海外 VPS + Caddy/Nginx

## 6. 日常检查

```powershell
Invoke-RestMethod http://127.0.0.1:5099/health/live
Invoke-RestMethod http://127.0.0.1:5099/health/ready
Invoke-RestMethod http://127.0.0.1:5099/api/v1/version
```

GitHub Webhook 验证：

- PR 打开/更新
- 检查 PR 是否出现 AgentOS Review
- 检查 commit status `agentos/quality-gate`

## 7. 重启

```powershell
Get-CimInstance Win32_Process |
  Where-Object { $_.CommandLine -like '*E:\AgentOS.Prod\AgentOS.Api.dll*' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

& E:\AgentOS.Prod\start-agentos.ps1
```

## 8. 已知限制

1. 使用 Quick Tunnel，URL 会变；依赖 Watchdog 自动更新 Webhook。
2. 登录启动，不是 Windows 服务；未登录时 API 不运行。
3. Development 环境 + 强 API Key，不是完整多用户认证。
4. 前端静态站点尚未由 API 托管。
5. 未配置 HTTPS 到 API（TLS 由 Cloudflare 终止）。
6. 备份任务已创建，但未做自动恢复演练。
7. 单实例、无高可用。

## 9. 第二阶段补充（2026-11-09）

### 前端静态托管

API 已启用 `UseDefaultFiles` / `UseStaticFiles` / SPA fallback。

前端构建产物位于：

```text
E:\AgentOS.Prod\wwwroot
```

访问：

```text
http://127.0.0.1:5099/
https://choosing-yesterday-couples-lows.trycloudflare.com/
```

更新前端：

```powershell
cd E:\Projects\AgentOS\frontend\agentos-web
$env:VITE_API_BASE_URL = ''
$env:VITE_DEV_API_KEY = [Environment]::GetEnvironmentVariable('Authentication__DevelopmentApiKey','User')
npm run build
robocopy dist E:\AgentOS.Prod\wwwroot /MIR /NFL /NDL /NJH /NJS /NP
```

### 质量门禁定时对账

任务计划：

```text
AgentOS Quality Reconcile
每 10 分钟
```

脚本：

```text
E:\AgentOS.Prod\reconcile-quality-gates.ps1
```

作用：查询打开 PR，逐个调用 `quality-gates/reevaluate`，补发漏掉的 `agentos/quality-gate`。

### 自动合并

当前未启用 AgentOS 自动合并；分支保护要求 0 个审批 + `agentos/quality-gate`。

建议先用 GitHub 原生 Auto-merge：

1. 仓库 Settings → General → Allow auto-merge
2. PR 页面启用 Auto-merge
3. 满足 Required Check 和审批后 GitHub 自动合并

如需 AgentOS 自动启用/执行合并，需要新增受控 merge API，属于后续任务。

### 固定公网入口

当前仍是 Quick Tunnel，Watchdog 自动更新 GitHub Webhook。固定 Named Tunnel 需要域名/Cloudflare 账号；属于外部配置项。
## 12. Quick Tunnel URL 影响与自动合并（2026-11-09）

### URL 变化影响

- 已创建任务、Worker、数据库、Codex 执行不受 URL 影响。
- GitHub 新事件依赖 Webhook URL；Watchdog 最多 5 分钟更新。
- 空窗期内 push / pull_request / workflow_run 可能投递失败，GitHub 不自动重试。
- PR 审查和质量门禁可通过对账部分恢复；push 类事件需手动 Redeliver。

### Auto-merge

- 仓库已开启 `allow_auto_merge=true`。
- 已创建 `enable-auto-merge.ps1` 和任务计划 `AgentOS Auto Merge`（每 5 分钟）。
- 脚本对带 `automerge` 标签的打开 PR 调用 GraphQL `enablePullRequestAutoMerge`。
- 当前 PAT 返回 `FORBIDDEN: Resource not accessible by personal access token`，需要：
  - PAT 增加 Contents: Write（允许合并），或
  - 在 GitHub PR 页面手动启用 Auto-merge。
- 测试 PR #2 已创建，标签 `automerge`，auto-merge 未成功启用。

### 当前采用方案 B（手动启用 Auto-merge）

- 已禁用计划任务 `AgentOS Auto Merge`，避免无权限重试。
- 仓库 `allow_auto_merge=true` 保持开启。
- 操作流程：
  1. 打开 PR
  2. 添加 `automerge` 标签（可选，仅作为标记）
  3. 在 PR 页面点击 **Enable auto-merge**
  4. 选择 Squash
  5. 等待 Required Check 和审批满足后 GitHub 自动合并
- AgentOS 只负责审查和质量门禁，不直接执行合并。
## 13. 改为 0 审批（2026-11-09）

- GitHub master 分支保护 required approvals 改为 0。
- `MergePolicies.RequiredApprovals` 改为 0。
- PR #2 head `1f9158bc...` Merge Readiness：
  - ready=true
  - mergeable=clean
  - approvals=0/0
  - quality-gate=success
  - branch-protection=required
- Auto-merge 仍需在 PR 页面点击 **Enable auto-merge**；当前 PAT 不能调用 GraphQL 自动启用。
## 14. 首次 Auto-merge 验证（2026-11-09）

- PR #2 已合并：
  - merged=true
  - merged_at=2026-11-08T18:14:09Z
  - merge commit d13125445e9c821a9eae1f5d22d3100e9bf8c851
- PR #2 有 1 条 AgentOS Review。
- 合并后 CI（merge commit）：success。
- 已删除测试文件 `auto-merge-smoke.txt`（commit 108fbf5）。
- 分支保护审批要求保持 0。
- 结论：个人单用户自动合并链路已跑通。