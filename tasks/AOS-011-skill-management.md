# AOS-011：Skill 管理与版本

状态：Completed
优先级：P0
依赖：AOS-004（Rule 引用 Skill）、AOS-007（Skill 内容进入 Prompt）、AOS-010（模型配置）

## 1. 目标

把 Skill 从"只能预置数据库 + 手放文件"升级为可管理、可版本化的 SOP 注册中心：
通过 API 创建新版本、保存 Markdown 内容、计算内容哈希、启用/禁用版本，并保持规则按
最新版本解析。

## 2. 范围

### 2.1 数据与存储

- 复用 `Skills` 表：`Code`、`Version`、`RelativePath`、`ContentHash`、`Description`、`IsEnabled`。
- 新增全局版本唯一索引（`ProjectId IS NULL` 时 `Code + Version` 唯一），迁移 `AddSkillGlobalVersionIndex`。
- 内容存储：`Skills:RootPath/<projectId|_global>/<code>/<version>.md`，路径由服务端生成；
  客户端不得提交路径。写入时计算 SHA-256 并回填 `ContentHash`。

### 2.2 API

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/v1/skills?projectId&code&includeDisabled&limit&offset` | 列表 |
| GET | `/api/v1/skills/{id}` | 元数据 |
| GET | `/api/v1/skills/{id}/content` | Markdown 内容 |
| POST | `/api/v1/skills` | 创建版本（Code/Version 不可变） |
| PUT | `/api/v1/skills/{id}` | 更新描述 |
| POST | `/api/v1/skills/{id}/enable\|disable` | 启停版本 |

### 2.3 校验

- Code：小写 slug（3–120），不可变。
- Version：字母/数字开头，`[0-9A-Za-z._-]`，≤40，不可变；同项目（或全局）唯一。
- Content：非空 Markdown，≤256KB；服务端计算 SHA-256。
- Description：≤1000。
- ProjectId：存在且启用；未配置 `Skills:RootPath` 时创建返回 503（避免写入任意路径）。

### 2.4 规则集成

- 既有 `RuleReferenceLookup.FindSkillAsync` 继续按项目优先、`CreatedAtUtc` 最新解析；
  禁用最新版本会阻止新规则引用（保持现有行为），历史任务不受影响。

## 3. 不在本任务范围

- Skill 内容 Diff/审批流、语义检索/向量化。
- 版本回滚（可重新启用旧版本，不删除历史）。
- 文件上传 multipart（第一版使用 JSON Markdown 内容）。
- 用户级权限（沿用开发 API Key）。

## 4. 安全

- 内容写入只能落在 `Skills:RootPath` 之下；客户端不能提交存储路径。
- Code/Version 做严格字符校验，避免路径穿越；写入前规范化并校验根目录包含关系。
- 内容大小限制；响应 `text/markdown` 下载需要 API Key。
- 不保存任何密钥；Skill 内容视为不可信数据，进入 Prompt 时已被转义。

## 5. 验收

- [ ] 可创建 Skill 新版本并写入文件，返回 SHA-256。
- [ ] 同项目 Code+Version 重复返回 409；全局版本也唯一。
- [ ] 列表可按项目/Code/是否禁用过滤。
- [ ] 可读取内容、启停版本、更新描述。
- [ ] 路径穿越/非法版本/超大内容返回 400。
- [ ] 后端 build/test（含 SQL）与前端 lint/build 通过。
- [ ] 文档与交付报告更新。

## 6. 交付

创建 `docs/reports/AOS-011-DELIVERY-REPORT.md`。
