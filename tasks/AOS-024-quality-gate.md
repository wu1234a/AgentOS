# AOS-024：CI 测试和质量门禁

状态：Completed
优先级：P1
依赖：AOS-022、AOS-023
来源：Kimi Planner 草案 `01a1b6ef-49fd-7c5a-b699-55864e2ab87b`（已批准）

## 0. 修订

Kimi 草案含 Actions runs 查询、重评估 API、分支保护自动配置。本阶段先交付：

1. 基于 `workflow_run` Webhook 累积每个 head SHA 的必需 workflow 结果。
2. 评估 `pending/success/failure`，发布 `agentos/quality-gate` commit status。
3. 配置按仓库声明必需 workflow 与允许结论。
4. 不自动配置分支保护（文档说明）、不查询 Actions、不自动 merge/approve。

## 1. 范围

- `QualityGateRuns`：repo/SHA/workflow 唯一，保存最新 run/status/conclusion/attempt。
- `QualityGateEvaluations`：repo/SHA 唯一，保存 gate status/reason。
- `GitHub:QualityGates`：`{ repo: { Enabled, RequiredWorkflows, AllowedConclusions } }`。
- Webhook `workflow_run`：更新 run，计算：
  - 缺必需 workflow / 未完成 → pending
  - 任一完成结论不在允许集合 → failure
  - 全部允许 → success
- 通过 `IGitHubCommitStatusClient` 发布 `agentos/quality-gate`。
- 迁移 `AddQualityGates`。

## 2. 不在范围

- 自动 merge/approve、自动重跑 workflow、Actions 日志解析。
- 查询 Actions runs、分支保护自动配置、配置后台。
- 多 CI 提供商、check_run 粒度。

## 3. 安全

- 仅消费已验签 webhook；token 仅环境变量。
- 固定 context `agentos/quality-gate`；target 仓库/SHA 绑定。
- 失败/缺失/异常不得发布 success；发布失败只记录，不伪造成功。
- 不调用 merge/approve/分支保护写接口。

## 4. 验收

- [x] 必需 workflow 全部成功 → success。
- [x] 缺 workflow/运行中 → pending；失败结论 → failure。
- [x] commit status 发布；run 重跑取最新 runId。
- [x] 后端全量测试、CI 通过。
