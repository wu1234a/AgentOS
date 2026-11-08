# AOS-028b / AOS-024c 后续补强

状态：Completed
来源：已批准的后续建议

## AOS-028b 项目过滤（部分）

- `GET /api/v1/coding-runs` 新增可选 `projectId`，在仓储 SQL 层过滤。
- 任务列表 `/api/v1/tasks` 已由 Observability 提供，本阶段未重复实现 projectId 过滤（避免路由冲突）。
- Agent 列表原有 projectId 过滤保持不变。
- 测试回归通过。

## AOS-024c 无 run 时 pending

- `QualityGateService.ReevaluateAsync` 在 GitHub Actions 没有任何必需 workflow run 时：
  - upsert `QualityGateEvaluations` 为 `pending`；
  - reason `required-workflow-missing`；
  - 发布 `agentos/quality-gate=pending`，不再静默返回成功。
- 质量门禁用例覆盖“无 run → pending”。

## 验证

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **330 通过 / 5 跳过 / 0 失败** |
| CI | 见对应 run |

## 已知限制

1. 任务列表 projectId 过滤未实现（Observability 端点需单独扩展）。
2. 规则列表 projectId 过滤未验证。
3. 前端未变更。

## 变更文件

`CodingRunRepository/Contracts/Service/Endpoints`、`QualityGateService`、
`SqlQualityGateTests`、测试 fake 签名、文档。
