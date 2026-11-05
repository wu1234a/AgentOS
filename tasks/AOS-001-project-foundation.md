# AOS-001：Project Foundation

状态：Not Started  
优先级：P0  
依赖：无  
预计范围：工程骨架和基础运行验证

## 1. 目标

建立 AgentOS
独立仓库的后端、前端、数据库访问和基础开发规范。完成后，开发者可以分别启动
ASP.NET Core API 和 Vue 3 JavaScript 管理后台，API 能连接 SQL Server。

## 2. 开始前必须阅读

- `README.md`
- `ROADMAP.md`
- `docs/ARCHITECTURE.md`
- `docs/DATABASE-ER.md`
- `docs/SECURITY-AND-OPERATIONS.md`
- `docs/CODEX-IMPLEMENTATION-GUIDE.md`

## 3. 技术要求

后端： - .NET 10 - ASP.NET Core Minimal API - EF Core SQL Server
Provider - 分层项目结构

前端： - Vue 3 + JavaScript + Vite - Pinia、Vue Router、Axios、Element
Plus - ESLint - 不使用 TypeScript

数据库： - SQL Server - EF Core Code First - Migration

## 4. 建议目录结构

``` text
AgentOS/
├── backend/
│   ├── AgentOS.sln
│   ├── AgentOS.Api/
│   ├── AgentOS.Domain/
│   ├── AgentOS.Application/
│   ├── AgentOS.Infrastructure/
│   ├── AgentOS.Worker/
│   ├── AgentOS.Runner/
│   └── AgentOS.AgentAdapters/
├── frontend/
│   └── agentos-web/
├── config/
├── skills/
├── projects/
├── policies/
├── docs/
├── tasks/
├── tests/
└── README.md
```

如果当前 .NET SDK 对 solution 文件格式有默认差异，使用当前 SDK
推荐格式，但保持所有项目可由统一 solution 管理。

## 5. 实施范围

### 后端

1.  创建 .NET Solution。
2.  创建
    Api、Domain、Application、Infrastructure、Worker、Runner、AgentAdapters
    项目。
3.  建立项目引用方向，Domain 不依赖 Infrastructure。
4.  配置 EF Core SQL Server 和 DbContext。
5.  配置开发环境连接字符串读取方式。
6.  增加 `/health/live` 和 `/health/ready`。
7.  Ready 检查应验证数据库连接；Live
    不应因数据库短暂故障而误报进程已死亡。
8.  配置统一 ProblemDetails 错误响应。
9.  配置结构化日志。
10. 建立最小
    Migration（可为空或仅建立基础迁移历史，不要提前创建后续全部业务表）。

### 前端

1.  使用 Vite 创建 Vue 3 JavaScript 项目。
2.  配置 Vue Router、Pinia、Axios、Element Plus。
3.  创建主布局、侧边栏、顶部栏和 Dashboard 占位页面。
4.  创建统一 Axios 实例和错误处理。
5.  创建 API 健康状态展示。
6.  配置 ESLint。
7.  确认项目没有 TypeScript 依赖、`.ts`、`.tsx` 或 `lang="ts"`。

### 工程

1.  添加 `.gitignore`。
2.  添加 `.editorconfig`。
3.  添加 `.env.example`，只放非敏感变量名和示例值。
4.  添加本地开发说明。
5.  建立测试项目。
6.  增加基础 CI：后端 build/test、前端 lint/build。

## 6. 不在本任务范围

- 事件接收。
- 规则引擎。
- 任务队列。
- Runner 实际领取任务。
- Codex CLI 调用。
- 飞书。
- GitHub 自动合并和发布。
- 生产环境部署。

## 7. 验收标准

- [ ] Solution 可正常构建。
- [ ] API 可启动。
- [ ] `/health/live` 返回成功。
- [ ] SQL Server 可连接时 `/health/ready` 返回成功。
- [ ] SQL Server 不可连接时 Ready 返回非成功状态且有可读错误。
- [ ] Vue 开发服务器可启动。
- [ ] Dashboard 可显示 API 健康状态。
- [ ] 前端 `npm run lint` 通过。
- [ ] 前端 `npm run build` 通过。
- [ ] 后端 `dotnet build` 和 `dotnet test` 通过。
- [ ] 全仓库无 `.ts`、`.tsx` 和 `lang="ts"`。
- [ ] 没有提交真实密钥或连接密码。

## 8. 交付

创建
`docs/reports/AOS-001-DELIVERY-REPORT.md`，记录目录、启动命令、数据库配置、测试结果和已知限制。

完成后停止，不要自动开始 AOS-002。
