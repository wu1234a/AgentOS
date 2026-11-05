# AOS-001 独立代码审查记录

- **审查对象：** commit `a6fe2d6`（AOS-001 初始交付）
- **审查方式：** `dsh_code_review` —— Codex 审查 rubric 的 DSH 原生实现，独立子 Agent，只读
- **审查环境：** 在临时导出的提交快照中执行构建/测试；未修改工作区
- **结论：** 无 Blocker / High；2 个 Medium、4 个 Low

## 1. 审查发现与处理

| 级别 | 问题 | 位置 | 处理 |
|---|---|---|---|
| Medium | 基础 `appsettings.json` 内置开发连接串，非 Development 环境可能静默连到开发库，且 `/health/ready` 会误报 Healthy | `backend/AgentOS.Api/appsettings.json` | 连接串移到 `appsettings.Development.json`；基础文件不再包含连接串；非 Development 缺少配置时启动快速失败 |
| Medium | CI 的 TypeScript 门禁在 `bash -eo pipefail` 下，`git ls-files \| grep -q .` 可能因 SIGPIPE 被判为通过 | `.github/workflows/ci.yml` | 改为命令替换 `ts_files="$(git ls-files '*.ts' '*.tsx')"`，按非空判断 |
| Low | 设计时工厂硬编码连接串并绕过 appsettings，可能与运行时配置漂移 | `AgentOSDbContextFactory.cs` | 改为 `ConfigurationBuilder` 读取 appsettings + 环境变量；支持从 API 项目目录解析 |
| Low | 缺少“数据库故障时 live 仍 200”和“503 响应不泄露配置”的回归测试 | `tests/AgentOS.Api.Tests/HealthEndpointTests.cs` | 新增 2 个测试；测试总数 4 → 6 |
| Low | 缺少 HTTPS 强制点 | `Program.cs`、`docs/DEVELOPMENT.md` | 非 Development 启用 `UseHttpsRedirection`；文档说明生产由反向代理终止 TLS |
| Low | `.env.example` 注释暗示 .NET 会读取 `.env` | `.env.example` | 改为说明使用 shell 环境变量或 `dotnet user-secrets` |

## 2. 复审结果

| 检查 | 结果 |
|---|---|
| `dotnet build backend/AgentOS.slnx` | 0 警告 / 0 错误 |
| `dotnet test backend/AgentOS.slnx` | 6 通过 / 0 失败 / 0 跳过 |
| `GET /health/live` | 200 Healthy |
| `GET /health/ready` | 200，`sqlserver` Healthy（180ms） |
| `dotnet ef migrations list --no-connect` | `20261105184054_InitialCreate` |
| `npm run lint` / `npm run build` | 通过（build 有 Element Plus 体积提示） |
| TypeScript 扫描 | 无 `.ts` / `.tsx` / `lang="ts"` |

## 3. 关于 Codex

本次审查使用的是 `dsh_code_review`，其 rubric 从 Codex 的 review 实现移植而来，**不依赖
Codex CLI**。当前 `agentos` profile 没有安装 `dsh-plugin-subagent-bridge`，因此没有直接
调用 Codex CLI；如需要真正的 Codex 二次审查，可在该 profile 安装该插件后使用
`codex_dispatch`。

## 4. 未处理的已知项

- 浏览器级 Dashboard 交互未做自动化 E2E；已通过 Vite 代理实测 `/health/live`、
  `/health/ready` 返回 Healthy。
- CI 中仍用替身健康检查，不含真实 SQL Server service container；真实数据库验收在本地完成。
