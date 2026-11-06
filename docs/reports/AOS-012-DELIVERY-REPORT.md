# AOS-012 交付报告：Planner Agent 与 Kimi 接入

**状态：Completed**
**完成时间：2026-11-07**
**任务文件：** `tasks/AOS-012-planner-agent.md`

## 1. 能力

- 服务端 Planner：输入标题/目标/上下文，调用 Kimi（或 OpenAI 兼容 API）生成 Markdown
  任务草案。
- 草案保存为 `PendingReview`，人工批准/拒绝；批准只改状态，不自动写文件或创建任务。
- 通用 OpenAI 兼容客户端，不绑定 Kimi；`provider`/`baseUrl` 来自受信 ModelProfile。

## 2. 数据与 API

- 迁移 `AddPlanningDrafts`：`PlanningDrafts`（标题、目标、上下文、模型、状态、
  ContentMarkdown、决策时间）。
- `GET /api/v1/planner/drafts`、`GET /{id}`、`POST /api/v1/planner/drafts`、
  `POST /{id}/approve|reject`。
- 错误：400 校验/模式不符、404、409 已决策、502 Provider 失败、503 缺密钥/默认 Profile。

## 3. Kimi 配置

- ModelProfile：`provider=kimi`、`executionMode=api`、`model=<Kimi 模型 ID>`、
  `configurationJson={"baseUrl":"https://api.moonshot.cn/v1"}`（K3 系列仅允许 temperature=1，建议省略）、`secretReference=KIMI_API_KEY`。
- 环境变量 `KIMI_API_KEY` 只在 API 进程读取；不入库、不写日志。
- `Planner:DefaultModelProfileCode`（默认 `kimi-planner`）与
  `Planner:AllowedProviderHosts`（默认 Moonshot/OpenAI）。

## 4. 安全

- 仅 HTTPS 与主机白名单；拒绝任意 URL。
- 目标/上下文作为数据段，系统提示声明不得覆盖安全规则；内容长度上限 100k。
- Provider 失败返回 502 且不落草案；密钥缺失 503；非 API Profile 400。
- 人工审批是唯一推进方式，避免自动实现。

## 5. 前端

`/planner`：列表/状态过滤、生成草案（标题/目标/上下文/模型 Profile）、查看 Markdown、
批准/拒绝。Vue 3 + JavaScript。

## 6. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **271 通过 / 5 跳过 / 0 失败** |
| Planner SQL 用例 | ✅ 2（生成/批准/拒绝/重复决策/列表；非法输入/缺密钥/CLI 模式） |
| Provider 客户端用例 | ✅ 5（请求格式/白名单/HTTP 错误/超时/畸形响应） |
| 前端 lint / build | ✅ 通过 |
| 真实 Kimi 调用 | 未在 CI 执行（需要真实 Key/网络），配置步骤见 DEVELOPMENT §20 |

### 6.1 独立代码审查\n\nCodex CLI 审查发现 1 个 P1（误删 Skill 全局唯一索引迁移）与 7 个 P2（决策竞态、HttpClient 超时、畸形响应、配置类型、前端超时/分页、重定向绕过、上下文限制），全部修复并新增测试，详见 `docs/reports/AOS-012-CODE-REVIEW.md`。\n\n## 7. 已知限制

1. 未做真实 Kimi 冒烟测试；需人工配置 Key 后验证模型 ID 与配额。
2. 草案批准后不会自动写入 `tasks/` 或创建任务；人工复制/后续 AOS-016 工作流。
3. 无流式输出、工具调用、重试/成本统计；Provider 失败需人工重试。
4. 主机白名单为静态配置，新增 Provider 需改配置重启。
5. Prompt 上下文仅来自请求，不自动读取 ROADMAP/架构文件（避免任意文件读取）。

## 8. 变更文件

**新增**：`Domain/Planning/PlanningDraft.cs`、`Application/Planning/*`、
`Infrastructure/Planning/*`、`Api/Endpoints/PlannerEndpoints.cs`、迁移 `AddPlanningDrafts`、
前端 `PlannerView.vue`/`api/planner.js`、`SqlPlannerTests.cs`、`ApiCompletionClientTests.cs`。

**修改**：`ModelProfileContracts.cs`/`ModelProfileRepository.cs`（FindByCode）、
`DependencyInjection.cs`、`Program.cs`、appsettings、router/layout、文档、任务状态。

## 9. 下一任务建议

AOS-013 Coding Agent 工作流：把批准后的草案转成实现任务；评估 Kimi 作为编码模型
（通用 API Adapter + 工具循环）；或先补 AOS-014 独立 Review Agent。
