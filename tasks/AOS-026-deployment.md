# AOS-026：部署、健康检查与线上版本核实

状态：Completed
优先级：P1
依赖：AOS-001 API、AOS-008 Observability、现有 health/CI
来源：Kimi Planner 草案 `01a1b743-ad0f-7690-8515-04fe7254ca52`（已批准）

## 0. 修订

Kimi 草案含构建注入 CI 修改、元数据测试、脚本自动化测试。本阶段先交付：

1. `/api/v1/version` 四项元数据。
2. 现有 live/ready 不改。
3. `scripts/Verify-Deployment.ps1` 只读核验。
4. `docs/deployment-local.md` 本地发布/核验步骤。
5. 不做自动部署、容器、CI 自动注入（可用环境变量）。

## 1. 范围

- `BuildMetadata.Get()`：version（AssemblyInformationalVersion）、commit（GITHUB_SHA/AGENTOS_GIT_COMMIT）、buildTime（AGENTOS_BUILD_TIME 或产物文件时间）、environment。
- `GET /api/v1/version` 返回 JSON。
- `Verify-Deployment.ps1`：GET 版本端点，校验四个字段与 commit，匹配退出 0，否则非零。
- 文档：本地发布、启动、health、版本核验、故障排查。

## 2. 不在范围

- 自动部署/重启/回滚、容器/K8s、监控平台。
- CI 自动写入构建元数据（当前用环境变量/产物时间）。
- 前端版本页。

## 3. 安全

- 仅四项非敏感字段；不返回路径/密钥/环境变量全集。
- 脚本只读；无发布/重启/覆盖操作。
- 不关闭 TLS 校验。

## 4. 验收

- [x] 版本端点稳定字段。
- [x] 脚本 commit 匹配/不匹配退出码。
- [x] live/ready 不变。
- [x] 后端全量测试、CI 通过。
