# AOS-010：Model Router / Provider Profiles

状态：Completed
优先级：P0
依赖：AOS-004（Rule.ModelProfileId）、AOS-009（Agent.DefaultModelProfileId）、AOS-007（Codex Adapter）

## 1. 目标

建立可管理的模型/Provider 注册表与解析规则：任务执行时按
`Task.ModelProfileId → Rule.ModelProfileId → Agent.DefaultModelProfileId` 的优先级解析出
有效模型配置，并交给 Runner/Adapter 使用。只保存 Provider 参数与密钥**引用**，不保存明文密钥。

## 2. 范围

### 2.1 API

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/v1/model-profiles?enabled&limit&offset` | 列表 |
| GET | `/api/v1/model-profiles/{id}` | 详情 |
| POST | `/api/v1/model-profiles` | 创建 |
| PUT | `/api/v1/model-profiles/{id}` | 更新（Code 不可变） |
| POST | `/api/v1/model-profiles/{id}/enable\|disable` | 启停 |
| GET | `/api/v1/model-profiles/resolve?taskId&ruleId&agentId` | 有效配置解析 |

### 2.2 校验

- Code：小写 slug（3–80），全局唯一、不可变。
- Provider / Model：必填，≤100。
- ExecutionMode：`cli` 或 `api`。
- ConfigurationJson：JSON 对象，≤8000 字符。
- SecretReference：可空；只允许环境变量名（`^[A-Z][A-Z0-9_]{2,80}$`），拒绝密钥值。
- TimeoutSeconds：1–7200；MaxCostPerTask ≥ 0。
- 被禁用或不存在时解析跳过，继续回退。

### 2.3 解析规则

- Task 级覆盖 Rule 级，Rule 级覆盖 Agent 默认；全部不可用时返回 404 `model-profile-unresolved`。
- 解析结果为只读视图：ProfileCode、Provider、Model、ExecutionMode、ConfigurationJson、
  SecretReference、TimeoutSeconds。

## 3. 不在本任务范围

- 真正调用 Kimi/OpenAI API；Adapter 仍由 Runner 执行。
- Claim/Adapter 参数下发改到 AOS-010b（Runner 接收解析结果）。
- Provider 密钥托管（仅引用环境变量名）。
- 计费/配额（AOS-029）。

## 4. 安全

- 管理端点需要 API Key；SecretReference 只存名称，响应中不含任何密钥值。
- ConfigurationJson 不得包含密钥字段；服务端拒绝明显的 `api_key`/`secret`/`password` 字段。
- 解析对禁用 Profile 自动跳过，避免误用下线模型。

## 5. 验收

- [ ] ModelProfile CRUD + 启停可用，Code 唯一且不可变。
- [ ] SecretReference 只接受环境变量名；ConfigurationJson 非法/含密钥字段返回 400。
- [ ] 解析优先级 task > rule > agent，禁用项跳过。
- [ ] SQL 测试与后端全量测试通过。
- [ ] 文档与交付报告更新。

## 6. 交付

创建 `docs/reports/AOS-010-DELIVERY-REPORT.md`。
