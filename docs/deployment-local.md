# 本地部署与版本核验

## 1. 构建与测试

```powershell
cd E:\Projects\AgentOS
dotnet build backend\AgentOS.slnx --nologo
dotnet test tests\AgentOS.Api.Tests\AgentOS.Api.Tests.csproj
```

## 2. 发布到本地运行目录

```powershell
dotnet publish backend\AgentOS.Api -c Debug -o E:\AgentOS.Api.Run
```

该目录只作为授权本地运行目录；不要覆盖正在运行的服务。

## 3. 启动

```powershell
cd E:\AgentOS.Api.Run
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:Authentication__DevelopmentApiKey = "local-dev-key"
$env:GITHUB_SHA = (git -C E:\Projects\AgentOS rev-parse HEAD)
.\AgentOS.Api.exe
```

`GITHUB_SHA` 可选；未设置时版本端点 commit 为 `Unknown`，核验脚本会失败。

## 4. 健康检查

```powershell
Invoke-RestMethod http://localhost:5000/health/live
Invoke-RestMethod http://localhost:5000/health/ready
```

## 5. 版本核验

```powershell
$expected = (git -C E:\Projects\AgentOS rev-parse HEAD)
.\scripts\Verify-Deployment.ps1 -BaseUrl http://localhost:5000 -ExpectedCommit $expected
```

退出码 0 表示版本和 commit 匹配；不匹配、`Unknown`、HTTP 错误或超时均为非零。

## 6. 版本端点

`GET /api/v1/version` 返回：

```json
{
  "version": "1.0.0",
  "gitCommit": "<sha or Unknown>",
  "buildTimeUtc": "2026-11-08T...Z",
  "environment": "Development"
}
```

- `GITHUB_SHA` / `AGENTOS_GIT_COMMIT` 提供 commit。
- `AGENTOS_BUILD_TIME` 可覆盖构建时间。
- 不访问数据库，不返回密钥/路径。
