# AOS-026 交付报告：部署、健康检查与版本核实

**状态：Completed**
**完成时间：2026-11-08**
**任务文件：** `tasks/AOS-026-deployment.md`

## 1. 能力

- `GET /api/v1/version`：
  - version、gitCommit、buildTimeUtc、environment
  - 不访问数据库
- `scripts/Verify-Deployment.ps1`：
  - 校验 HTTP 200、四个字段、commit 匹配
  - 匹配退出 0；不匹配/Unknown/异常/超时非零
- `docs/deployment-local.md`：本地发布、启动、健康检查、核验步骤
- 现有 `/health/live` `/health/ready` 不变

## 2. 配置

- `GITHUB_SHA` 或 `AGENTOS_GIT_COMMIT`：commit
- `AGENTOS_BUILD_TIME`：构建时间（可选，默认产物文件时间）
- `ASPNETCORE_ENVIRONMENT`：环境

## 3. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **316 通过 / 5 跳过 / 0 失败** |
| 版本元数据用例 | ✅ 四个字段非空 |
| CI | 见对应 run |

## 4. 已知限制

1. 未在 CI 构建时自动写入 commit/buildTime；运行环境需设置或显示 Unknown/文件时间。
2. 无自动部署/容器/回滚。
3. 未新增版本端点 HTTP 集成测试（仅元数据单元测试）。
4. 脚本未做 Pester 自动化测试。
5. 未执行真实线上部署核验。

## 5. 变更文件

`BuildMetadata.cs`、`VersionEndpoints.cs`、Program、`scripts/Verify-Deployment.ps1`、
`docs/deployment-local.md`、`BuildMetadataTests.cs`、文档。
