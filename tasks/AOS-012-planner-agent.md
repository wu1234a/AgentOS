# AOS-012：Planner Agent 与 Kimi 接入

状态：Completed
优先级：P0
依赖：AOS-010（ModelProfile）、AOS-010b（模型下发）、AOS-011（Skill）

## 1. 目标

新增服务端 Planner：输入目标/上下文，调用 Kimi（或任意 OpenAI 兼容 API）生成任务文件草案；
草案进入 `PendingReview`，必须人工批准后才可进入实现，不自动写文件或创建任务。

## 2. 范围

- `PlanningDrafts` 表（迁移 `AddPlanningDrafts`）：标题、目标、上下文、模型信息、状态、
  Markdown 内容、决策时间。
- 通用 `IApiCompletionClient`：OpenAI 兼容 `/chat/completions`，仅允许
  `Planner:AllowedProviderHosts` 中的 HTTPS 主机；密钥来自 `SecretReference` 环境变量。
- Planner API：
  - `GET /api/v1/planner/drafts?status&limit&offset`
  - `GET /api/v1/planner/drafts/{id}`
  - `POST /api/v1/planner/drafts`（生成，返回 PendingReview）
  - `POST /{id}/approve`、`POST /{id}/reject`
- `PlannerService`：校验目标/上下文，解析 API 模式 Profile（显式或默认
  `Planner:DefaultModelProfileCode`），构造可信系统提示 + 数据段，调用 Provider。
- 前端 `/planner` 页面：生成草案、查看 Markdown、批准/拒绝。

## 3. 不在本任务范围

- 自动创建 `tasks/AOS-XXX.md` 或自动实现；批准仅改变草案状态。
- 编码执行使用 Kimi（AOS-013 之后评估）。
- 流式输出、工具调用、费用统计。
- 密钥托管（只引用环境变量名）。

## 4. Kimi 配置示例

ModelProfile：`provider=kimi`、`executionMode=api`、`model=<Kimi 控制台模型 ID>`、
`configurationJson={"baseUrl":"https://api.moonshot.cn/v1","temperature":0.2}`、
`secretReference=KIMI_API_KEY`；`Planner:DefaultModelProfileCode=kimi-planner`。

## 5. 安全

- 仅 HTTPS + 主机白名单；不跟随任意 URL。
- 密钥只从环境变量读取，不写库、不写日志、不回传。
- 目标/上下文视为不可信数据，系统提示明确不得覆盖安全规则。
- 草案必须人工批准；Provider 失败返回 502 且不落草案。
- 生成内容长度上限 `Planner:MaxDraftCharacters`。

## 6. 验收

- [ ] 可用 Kimi/兼容 Profile 生成 PendingReview 草案。
- [ ] 草案可批准/拒绝，重复决策返回 409。
- [ ] 非 API 模式、缺失密钥、非法主机、Provider 失败有明确错误。
- [ ] 后端全量测试、前端 lint/build 通过。
- [ ] 文档与交付报告更新。

## 7. 交付

创建 `docs/reports/AOS-012-DELIVERY-REPORT.md`。
