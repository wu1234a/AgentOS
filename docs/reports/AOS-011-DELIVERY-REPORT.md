# AOS-011 交付报告：Skill 管理与版本

**状态：Completed**
**完成时间：2026-11-07**
**任务文件：** `tasks/AOS-011-skill-management.md`

## 1. 数据与存储

- 复用 `Skills` 表，新增全局版本唯一索引（迁移 `AddSkillGlobalVersionIndex`）。
- 内容路径：`Skills:RootPath/<projectId|_global>/<code>/<version>.md`，服务端生成。
- 写入时计算 SHA-256 写入 `ContentHash`；版本只增不改。

## 2. API

- `GET /api/v1/skills`（projectId / code / includeDisabled / 分页）
- `GET /api/v1/skills/{id}`、`GET /{id}/content`（text/markdown）
- `POST /api/v1/skills`（201；Code/Version 不可变）
- `PUT /api/v1/skills/{id}`（描述）
- `POST /api/v1/skills/{id}/enable|disable`
- 错误：400 校验、404 不存在/内容缺失、409 `skill-version-conflict`、
  503 `skill-storage-unavailable`。

## 3. 校验与安全

- Code（3–120 小写 slug）、Version（≤40，字符集受限）、Description（≤1000）。
- 内容非空且不超过 `Skills:MaxBytes`；写入根目录包含校验，客户端不能指定路径。
- 同项目/全局 Code+Version 唯一；重复创建清理已写文件并返回冲突。
- 管理端点需要 API Key；内容下载需要 API Key。

## 4. 规则集成

- 既有 `RuleReferenceLookup` 继续按项目优先、`CreatedAtUtc` 最新选择版本；
  新增测试验证 1.1.0 覆盖 1.0.0。
- 禁用最新版本会阻止新规则引用（现有行为）；历史任务与 Prompt 内容不变。

## 5. 前端

`/skills`：列表、Code/禁用过滤、创建版本（Markdown）、查看内容、启用/禁用；
Vue 3 + JavaScript。

## 6. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **259 通过 / 4 跳过 / 0 失败** |
| AOS-011 SQL 用例 | ✅ 3（创建/哈希/重复/内容读取/描述/禁用；全局与项目唯一；最新版本与非法输入/未配置存储） |
| 前端 lint / build | ✅ 通过 |
| CI | 见对应 run |

## 7. 已知限制

1. Skill 内容无法通过 API 修改（版本不可变）；修改需创建新版本。
2. 无 Diff、审批流、回滚 UI；旧版本可重新启用但不删除。
3. 无 multipart 上传；内容通过 JSON Markdown 提交。
4. 无版本签名/来源校验；内容仍视为不可信并在 Prompt 中转义。
5. 全局唯一索引对历史重复数据可能迁移失败；生产升级前需先清理重复行。

## 8. 变更文件

**新增**：`Application/Skills/*`、`Infrastructure/Skills/SkillStore.cs`、`SkillRepository.cs`、
`Api/Endpoints/SkillsEndpoints.cs`、迁移 `AddSkillGlobalVersionIndex`、前端
`SkillsView.vue`/`api/skills.js`、`SqlSkillTests.cs`。

**修改**：`SkillConfiguration.cs`、`DependencyInjection.cs`、`Program.cs`、router/layout、
`DATABASE-ER.md`、`DEVELOPMENT.md`、任务状态。

## 9. 下一任务建议

AOS-010b（模型下发到 Runner）后进入 AOS-012 Planner Agent + Kimi 接入。
