# AgentOS 上线验收清单

状态：进行中
更新：2026-11-08

## 1. 自动化门禁

- [x] 后端构建 0 错误
- [x] 后端全量测试 331 通过 / 5 跳过
- [x] 前端 lint 通过
- [x] 前端 build 通过
- [x] CI run#95 SUCCESS

## 2. 本地运行时验收

- [x] `/health/live` 200
- [x] `/health/ready` 200（smoke：AgentOS_Test）
- [x] `/api/v1/version` 四字段完整（本地 commit=Unknown，符合无 build-info 预期）
- [x] GitHub Webhook 缺失签名 → 400；错误签名 → 401
- [x] GitHub Webhook 有效签名 → 202 accepted（smoke-3）
- [ ] 前端页面可访问：`/projects`、`/usage`、`/workers`、`/pipelines`（构建通过，浏览器验收未执行）

## 3. 真实 GitHub 联调（需仓库权限）

- [x] 配置 `GitHub__WebhookSecret`（User Secrets）
- [ ] 配置 `GitHub__ReviewToken`（当前 Token 401，待替换）
- [x] 配置 `GitHub__AllowedRepositories`
- [x] 配置 `GitHub__RepositoryProjects`
- [x] Webhook 事件：Pushes 已真实验证；Pull requests/Workflow runs 待触发
- [x] PR opened/synchronize 触发自动审查并生成 COMMENT Review（Review 5818049357）
- [ ] workflow_run 发布 `agentos/quality-gate`
- [ ] 分支保护要求 `agentos/quality-gate`（Token 缺 Administration 权限，未配置）
- [x] 合并就绪 API 返回 blocked（approvals 0/1，quality-gate success）
- [ ] 质量门禁漏事件后重评恢复

## 4. 配置检查

- [ ] `GitHub__ReviewToken` 仅环境变量
- [ ] `Knowledge__Embedding__ApiKey` 仅环境变量
- [ ] Telegram 配置可选且默认关闭
- [ ] 数据库连接串不含明文生产密码
- [ ] `ASPNETCORE_ENVIRONMENT=Production`
- [ ] 反向代理 HTTPS / Webhook 路径可达

## 5. 发布与回滚

- [ ] 数据库迁移已备份
- [ ] 发布产物版本与 commit 一致
- [ ] `scripts\Verify-Deployment.ps1` 返回 0
- [ ] 回滚产物可用
- [ ] 回滚后健康检查通过

## 6. 验收结论

| 项 | 结论 |
|---|---|
| 自动化 | 通过 |
| 本地运行时 | 通过（smoke） |
| 真实 GitHub | 待配置 Token/仓库 |
| 生产发布 | 未执行 |

## 7. 本地 smoke 记录（2026-11-08）

- API：`http://127.0.0.1:5099`，数据库 `AgentOS_Test`。
- `GET /health/live` → 200。
- `GET /health/ready` → 200，SQL Server Healthy。
- `GET /api/v1/version` → 200，version/buildTime/environment 正常，commit=Unknown（本地未生成 build-info.json）。
- 缺失签名 → 400。
- 错误签名 → 401。
- 有效签名 PR 事件 → 202 `accepted`。
- 项目 `agentos` 自动创建成功。
## 8. 真实 GitHub Webhook 验证（2026-11-08）

- 公网隧道：`https://impossible-affiliates-variables-pure.trycloudflare.com`
- GitHub Webhook 已添加，ping 返回 200。
- 推送测试分支触发真实 `push` 事件：
  - Delivery：`864b94aa-db75-11f1-8240-870ecfe6f49f`
  - 事件：`github.push`
  - 项目：`agentos`
  - 状态：`Ignored`（无匹配规则，符合预期）
- 测试分支：`acceptance/webhook-smoke-210836`（远程分支尚未删除）。
- PR 自动审查暂不可用：`GitHub:ReviewToken` 返回 401，已临时设置 `ReviewEnabled=false`。
## 9. 质量门禁与合并就绪验证（2026-11-08）

- PR #1 head：`31ee2cc63daa7e30fe4d5529f28440c4034a2e76`
- CI workflow `CI`：completed/success
- commit status：`agentos/quality-gate = success`
  - description：All required workflows passed.
  - 时间：2026-11-08T13:30:42Z
- Merge Readiness：
  - pr-open ✅
  - not-draft ✅
  - mergeable ✅
  - approvals ❌ 0/1
  - quality-gate ✅ success
  - 总体：blocked（符合预期）
- 分支保护核验：`unverified`，原因是当前 PAT 无 Administration 权限（HTTP 403）。