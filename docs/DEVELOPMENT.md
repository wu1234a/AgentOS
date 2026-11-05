# AgentOS 本地开发说明

版本：AOS-001 基线

## 1. 环境要求

| 组件 | 版本 | 说明 |
|---|---|---|
| .NET SDK | 10.0.x | 后端使用 ASP.NET Core Minimal API + EF Core |
| SQL Server | 2019+（本机为 2025） | 开发库 `AgentOS_Dev` |
| Node.js / npm | Node 24 / npm 11 | 前端 Vite + Vue 3 (JavaScript) |
| Git | 2.39+ | 仓库根目录 `E:\Projects\AgentOS` |

前端约束：**不使用 TypeScript**。不要创建 `.ts`/`.tsx`，不要在 `.vue` 中使用 `lang="ts"`。

## 2. 数据库

默认使用 Windows 身份连接本机默认实例，开发库为 `AgentOS_Dev`：

```text
Server=lpc:localhost;Database=AgentOS_Dev;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=3
```

说明：

- 本机 SQL Server 的 TCP 1433 与命名管道当前是关闭的，只开放共享内存；`lpc:localhost` 显式走共享内存。
- 如果换到 TCP 已开放的实例，把 `Server` 改成 `localhost,1433` 即可。
- 不要在代码或提交里写 SQL 密码；本地开发使用 Windows 身份或 User Secrets / 环境变量。

创建/更新数据库：

```powershell
dotnet ef database update --project backend/AgentOS.Infrastructure --startup-project backend/AgentOS.Api
```

AOS-001 的 `InitialCreate` 是空迁移，只创建 `__EFMigrationsHistory`；业务表从 AOS-002 开始。

## 3. 启动后端

```powershell
dotnet run --project backend/AgentOS.Api
```

默认地址：`http://localhost:5000`

健康检查：

- `GET /health/live`：进程存活，不依赖数据库。
- `GET /health/ready`：包含 SQL Server 连通性；数据库不可达时返回 503。

> 注意：如果通过 DSH（DeepSeek Harness）的受限子进程启动 API，进程可能是 **Low 完整性级别**，访问 SQL Server 命名管道会被拒绝（Win32 5）。请在普通 PowerShell（Medium 完整性）中启动 API，或从 DSH 之外的终端运行。构建、EF 迁移和前端命令不受影响。

## 4. 启动前端

```powershell
cd frontend/agentos-web
npm install
npm run dev
```

默认地址：`http://localhost:5173`。Vite 已把 `/api` 和 `/health` 代理到 `http://localhost:5000`，Dashboard 会显示 API Live / Ready 状态。

其他命令：

```powershell
npm run lint
npm run build
```

## 5. 测试与构建

```powershell
dotnet build backend/AgentOS.slnx
dotnet test backend/AgentOS.slnx
```

> `dotnet test` 会启动 testhost；在 DSH 的 Low 令牌下 testhost 可能因无法打开父进程句柄而中止（Win32 5）。请在普通 PowerShell 中运行测试。

前端：

```powershell
cd frontend/agentos-web
npm run lint
npm run build
```

## 6. 配置

- 后端：`backend/AgentOS.Api/appsettings.json`（无密码），可用环境变量 `ConnectionStrings__AgentOS` 覆盖。
- 前端：`frontend/agentos-web/.env.example` 中的 `VITE_API_BASE_URL`，默认留空走 Vite 代理。
- 根目录 `.env.example` 只列非敏感变量；不要提交真实密钥、连接密码或 PAT。

## 7. CI

`.github/workflows/ci.yml` 包含：

- 后端：`dotnet build` + `dotnet test`；
- 前端：`npm ci` + `npm run lint` + `npm run build`；
- 禁 TypeScript 门禁：拒绝 `.ts`、`.tsx` 和 `lang="ts"`。

CI 中的测试使用替身健康检查，不依赖 SQL Server；真实数据库的 `/health/ready` 验收在本地完成。
