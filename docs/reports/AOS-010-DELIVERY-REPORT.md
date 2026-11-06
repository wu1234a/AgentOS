# AOS-010 交付报告：Model Router / Provider Profiles

**状态：Completed**
**完成时间：2026-11-07**
**任务文件：** `tasks/AOS-010-model-router.md`（按 ROADMAP 起草并实施）

## 1. 目标

建立模型/Provider 注册表与解析规则，按 Task → Rule → Agent 顺序解析有效模型配置；
只保存密钥引用，不保存明文。

## 2. API

- `GET /api/v1/model-profiles`（enabled / limit / offset）
- `GET /api/v1/model-profiles/{id}`
- `POST /api/v1/model-profiles`（201，Code 全局唯一且不可变）
- `PUT /api/v1/model-profiles/{id}`
- `POST /api/v1/model-profiles/{id}/enable|disable`
- `GET /api/v1/model-profiles/resolve?taskId&ruleId&agentId`
- 错误：400 校验、404 不存在/未解析、409 `model-profile-code-conflict`。

## 3. 校验与安全

- Code 小写 slug（3–80），Provider/Model ≤60/120。
- ExecutionMode 仅 `cli`/`api`。
- ConfigurationJson 必须是 JSON 对象且 ≤8000 字符；禁止 `api_key`/`apikey`/`secret`/
  `password`/`access_token`/`client_secret` 字段。
- SecretReference 仅接受环境变量名；超时 1–7200；MaxCostPerTask ≥ 0。
- 管理端点要求 API Key；响应只返回密钥引用，不返回值。

## 4. 解析

优先级 `Task.ModelProfileId → Rule.ModelProfileId → Agent.DefaultModelProfileId`；
只使用启用的 Profile；全部不可用返回 `404 model-profile-unresolved`。
响应包含 ProfileCode/Provider/Model/ExecutionMode/ConfigurationJson/SecretReference/
TimeoutSeconds 与解析来源（task/rule/agent）。

## 5. 前端

新增 `/models` 页面：Profile 列表、创建/编辑对话框（Code 不可变）、启用/禁用；
密钥只填写环境变量名。Vue 3 + JavaScript。

## 6. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **256 通过 / 4 跳过 / 0 失败** |
| AOS-010 SQL 用例 | ✅ 2（CRUD/唯一 Code/密钥校验；三级解析与禁用回退） |
| 前端 lint / build | ✅ 通过 |
| CI | 见对应 run |

### 6.1 独立代码审查\n\nCodex CLI 审查发现 1 个 P1（嵌套密钥字段绕过校验）与 2 个 P2（rule-only 解析缺少 Agent 回退、前端编辑清空成本上限），全部修复并新增回归测试，详见 `docs/reports/AOS-010-CODE-REVIEW.md`。\n\n## 7. 已知限制

1. 解析结果尚未下发到 Claim/Runner；Codex Adapter 仍使用 Runner 配置的模型
   （AOS-010b）。
2. 不托管真实密钥，只引用环境变量；密钥轮换依赖 Runner/API 部署环境。
3. 前端无解析预览；解析通过 API 调用。
4. Provider 类型为自由文本，尚未做 Provider 插件/能力校验。\n5. `ConfigurationJson` 只禁止密钥字段名，无法识别任意编码的凭证值；密钥仍应只通过 `SecretReference` 引用。

## 8. 变更文件

**新增**: `Application/ModelProfiles/*`、`Infrastructure/ModelProfiles/ModelProfileRepository.cs`、
`Api/Endpoints/ModelProfilesEndpoints.cs`、前端 `ModelProfilesView.vue`/`api/modelProfiles.js`、
`SqlModelProfileTests.cs`。

**修改**: `DependencyInjection.cs`、`Program.cs`、router/layout、`DATABASE-ER.md`、
`DEVELOPMENT.md`、任务状态。

## 9. 下一任务建议

AOS-010b：Claim 携带解析后的 Model/Provider/ExecutionMode/Timeout，Runner 传给 Codex `-m`；
随后 AOS-011 Skill 管理与版本。
