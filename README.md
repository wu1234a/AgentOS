# AgentOS

Personal Multi-Agent Automation Platform

## 1. 项目定位

AgentOS 是一个独立的、多项目共用的 Agent
自动化平台。它通过统一事件入口接收任务，按规则匹配 Agent、Skill
和模型配置，由本地 Runner
在受控工作区执行任务，并将结果、日志、产物和审批记录统一归档。

核心原则：

1.  **事件驱动**：事件发生时触发工作，而不是依赖用户始终盯着聊天窗口。
2.  **规则优先**：简单任务通过规则路由，复杂任务才交给强推理模型规划。
3.  **模型可替换**：业务逻辑不绑定某一家模型或某一个 CLI。
4.  **执行隔离**：Agent 在受控工作区执行，不直接操作主工作区或生产环境。
5.  **人工把关**：删除数据、改权限、密钥操作、生产发布等高风险行为必须经过策略审批。
6.  **可追溯**：事件、任务、尝试、日志、产物、审批和决策均可查询。
7.  **渐进建设**：先完成事件接收、规则、队列和 Runner，再逐步扩展多
    Agent、审批和 CI。

## 2. 技术栈

### 后端

- ASP.NET Core Minimal API
- .NET 10
- Entity Framework Core
- SQL Server
- .NET Worker Service
- SignalR
- Quartz.NET（后续阶段启用）

### 前端

- Vue 3
- **JavaScript（不使用 TypeScript）**
- Vite
- Pinia
- Vue Router
- Axios
- Element Plus

前端约束： - Vue 文件使用 `<script setup>`，不添加 `lang="ts"`。 -
不创建 `.ts`、`.tsx` 文件。 - 业务代码使用 `.js`、`.vue`。 - 可使用
JSDoc 注释表达复杂对象结构，但不引入 TypeScript 编译链。

### 执行与工程

- Git / GitHub
- Git Worktree
- Windows + WSL2（推荐的本地执行环境）
- Docker（数据库及隔离执行环境按需使用）
- Codex CLI Adapter（首个编码 Agent 适配器）

## 3. 文档导航

| 文件                                   | 内容                                     |
|----------------------------------------|------------------------------------------|
| `ROADMAP.md`                           | 产品范围、阶段规划、AOS 里程碑           |
| `docs/ARCHITECTURE.md`                 | 系统架构、组件职责、运行流程、API 和部署 |
| `docs/DATABASE-ER.md`                  | 数据库 ER 图、表结构、索引和约束         |
| `docs/EVENT-SPECIFICATION.md`          | 统一事件协议、去重、重试和安全           |
| `docs/SECURITY-AND-OPERATIONS.md`      | 权限、密钥、隔离、恢复和运维规范         |
| `docs/CODEX-IMPLEMENTATION-GUIDE.md`   | Codex 执行每个 AOS 任务时必须遵守的规则  |
| `tasks/AOS-001-*.md` 至 `AOS-008-*.md` | 第一阶段开发任务                         |
| `config/examples/`                     | Agent 和规则配置样例                     |
| `skills/common/`                       | 公共 Skill 示例                          |

## 4. 推荐磁盘位置

将本项目放在：

``` text
E:\Projects\AgentOS\
```

建议同级项目布局：

``` text
E:\Projects\
├── AgentOS\
├── 3DERP\
└── EmperorSimulator\
```

AgentOS 必须拥有独立 Git 仓库。不要把 AgentOS 放入 `3DERP` 或
`EmperorSimulator` 仓库内部。

## 5. 第一阶段范围

AOS-001 至 AOS-008 完成后，应达到以下能力：

1.  管理后台和后端基础工程可以启动。
2.  API 可以接收统一事件。
3.  重复事件可以幂等去重。
4.  规则引擎可以将事件转换为待执行任务。
5.  SQL Server 任务队列支持安全领取、租约、超时和重试。
6.  本地 Runner 可以主动连接服务端、领取任务并回传状态。
7.  Runner 可以通过适配器调用 Codex CLI 执行受控任务。
8.  任务日志和产物元数据可以持久化并在后台查看。

AOS-001 至 AOS-008 **不代表**已经实现完整的多 Agent
自主协作、飞书审批或生产 CI/CD。它们属于后续里程碑。

## 6. 开发顺序

必须按顺序实施：

1.  阅读 `ROADMAP.md`、架构文档、数据库文档和安全规范。
2.  完成 `AOS-001` 并验收。
3.  完成 `AOS-002` 并验收。
4.  依次完成 `AOS-003` 至 `AOS-008`。
5.  每个任务完成后提交交付报告和测试证据。
6.  未通过当前任务验收，不得提前开始依赖它的后续任务。

## 7. Codex 工作约定

每次让 Codex 开发时： - 只执行一个 AOS 任务文件。 -
先检查仓库现状，不得假设目录或代码已经存在。 -
先输出实施计划，再修改文件。 - 不得擅自扩大任务范围。 -
不得删除或重写已有数据。 - 数据库变更必须使用 EF Core
Migration，并提供可回滚/恢复说明。 -
不得将密钥、Token、密码写入代码、配置样例或日志。 -
不得自动部署到生产环境。 - 完成后运行任务要求的测试，并生成交付报告。 -
如果发现需求冲突，暂停并说明，不得自行猜测。

## 8. 当前状态

项目文档基线已建立。实际代码开发从 `AOS-001` 开始。
