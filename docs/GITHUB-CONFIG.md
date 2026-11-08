# GitHub 配置位置

## 1. 配置来源与优先级

ASP.NET Core 按以下顺序加载，后面的覆盖前面的：

1. `backend/AgentOS.Api/appsettings.json`
2. `backend/AgentOS.Api/appsettings.{Environment}.json`
3. User Secrets（仅 Development）
4. 环境变量
5. 命令行参数

线上推荐 **环境变量** 或部署目录的 **appsettings.Production.json**；不要提交真实 Token/Secret。

## 2. 四个配置项

| 配置键 | 含义 | 示例 |
|---|---|---|
| `GitHub:WebhookSecret` | GitHub Webhook 的 Secret，自己生成 | `openssl rand -hex 32` |
| `GitHub:ReviewToken` | GitHub PAT，用于拉 PR/diff、发 Review、写 commit status | `github_pat_...` |
| `GitHub:AllowedRepositories` | 允许的仓库 full_name | `["wu1234a/AgentOS"]` |
| `GitHub:RepositoryProjects` | 仓库 full_name → AgentOS 项目 slug | `{"wu1234a/AgentOS":"agentos"}` |

另外必须打开：

```json
"GitHub": {
  "Enabled": true,
  "ReviewEnabled": true
}
```

`agentos` 项目必须已存在于 `Projects` 表，否则 Webhook 返回 503。

## 3. 本地开发配置

### 方式 A：User Secrets（推荐，不落盘仓库）

```powershell
cd E:\Projects\AgentOS

dotnet user-secrets --project backend\AgentOS.Api set "GitHub:Enabled" "true"
dotnet user-secrets --project backend\AgentOS.Api set "GitHub:ReviewEnabled" "true"
dotnet user-secrets --project backend\AgentOS.Api set "GitHub:WebhookSecret" "<secret>"
dotnet user-secrets --project backend\AgentOS.Api set "GitHub:ReviewToken" "<pat>"
dotnet user-secrets --project backend\AgentOS.Api set "GitHub:AllowedRepositories:0" "wu1234a/AgentOS"
dotnet user-secrets --project backend\AgentOS.Api set "GitHub:RepositoryProjects:wu1234a/AgentOS" "agentos"
```

查看：

```powershell
dotnet user-secrets --project backend\AgentOS.Api list
```

### 方式 B：appsettings.Development.json

放在 `backend/AgentOS.Api/appsettings.Development.json`：

```json
{
  "GitHub": {
    "Enabled": true,
    "ReviewEnabled": true,
    "WebhookSecret": "<secret>",
    "ReviewToken": "<pat>",
    "AllowedRepositories": [ "wu1234a/AgentOS" ],
    "RepositoryProjects": {
      "wu1234a/AgentOS": "agentos"
    }
  }
}
```

该文件当前已被 Git 跟踪，**不要把真实 Secret/Token 写进去**；真实凭据请用 User Secrets 或环境变量。

## 4. 生产/部署配置

### 方式 A：环境变量（推荐）

Windows 服务/PowerShell：

```powershell
$env:GitHub__Enabled = "true"
$env:GitHub__ReviewEnabled = "true"
$env:GitHub__WebhookSecret = "<secret>"
$env:GitHub__ReviewToken = "<pat>"
$env:GitHub__AllowedRepositories__0 = "wu1234a/AgentOS"
```

`RepositoryProjects` 的 key 含 `/`，环境变量名中不推荐直接使用；建议用方式 B 写 JSON。

### 方式 B：部署目录 appsettings.Production.json

发布目录（例如 `E:\AgentOS.Api.Run`）放置：

```json
{
  "GitHub": {
    "Enabled": true,
    "ReviewEnabled": true,
    "WebhookSecret": "<secret>",
    "ReviewToken": "<pat>",
    "AllowedRepositories": [ "wu1234a/AgentOS" ],
    "RepositoryProjects": {
      "wu1234a/AgentOS": "agentos"
    }
  }
}
```

启动时设置：

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
```

## 5. GitHub Token 权限

Fine-grained PAT，至少：

- **Contents: Read**
- **Pull requests: Read and write**
- **Commit statuses: Read and write**
- **Actions: Read**（质量门禁重评查询 runs）

不需要 Administration（分支保护只读核验需要读取权限时再按需授予）。

## 6. GitHub Webhook 设置

仓库 → Settings → Webhooks → Add webhook：

- Payload URL：`https://<你的域名>/api/v1/webhooks/github`
- Content type：`application/json`
- Secret：与 `GitHub:WebhookSecret` 相同
- Events：
  - Pull requests
  - Pushes
  - Workflow runs

本地联调可用 ngrok/cloudflared 暴露 API。

## 7. 质量门禁与合并策略（可选）

同一 `GitHub` 节点下：

```json
"QualityGates": {
  "wu1234a/AgentOS": {
    "Enabled": true,
    "RequiredWorkflows": [ "CI" ],
    "AllowedConclusions": [ "success" ]
  }
},
"MergePolicies": {
  "wu1234a/AgentOS": {
    "Enabled": true,
    "RequiredApprovals": 1,
    "RequireQualityGate": true,
    "RequireBranchProtection": false
  }
}
```

## 8. 验证配置是否生效

1. 启动 API。
2. 查看日志是否出现 GitHub Webhook 相关记录。
3. 在 GitHub 仓库 Webhook 页面点 **Redeliver** 一个 Pull request 事件。
4. 预期：
   - 返回 2xx
   - `PullRequestReviewJobs` 生成任务（`ReviewEnabled=true`）
   - PR 出现 `AgentOS 独立代码审查` 评论

## 9. 注意

- 不要把 `WebhookSecret`、`ReviewToken` 写入代码、日志、文档示例或提交。
- 修改配置后需要重启 API。
- `AllowedRepositories` 为空时 Webhook 拒绝所有仓库。
- `RepositoryProjects` 未映射时返回 503，不会静默忽略。
