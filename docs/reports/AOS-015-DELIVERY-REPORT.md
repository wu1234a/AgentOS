# AOS-015 交付报告：QA Agent（命令级结果）

**状态：Completed**
**完成时间：2026-11-07**
**任务文件：** `tasks/AOS-015-qa-agent.md`

## 1. 能力

- Runner 在 AOS-013b 校验后上传 `validation-results.json`（命令、状态、退出码、耗时、输出摘要）。
- 已终态（Succeeded/Failed）的 Coding Run 可手动创建 QA Run；读取既有结果，不重跑命令。
- `QaRuns` / `QaCommandResults` 持久化，每次 QA 生成新 Attempt。
- `/coding-runs` 详情 QA 面板：状态、结论、Attempt、命令表格与输出摘要。

## 2. 数据与 API

- 迁移 `AddQaAgent`：`QaRuns`（CodingRunId + AttemptNo 唯一）、`QaCommandResults`
  （QaRunId + Ordinal 唯一）。
- `POST /api/v1/coding-runs/{id}/qa-runs`：同步读取 validation-results 并返回 QA。
- `GET /api/v1/coding-runs/{id}/qa-runs/latest`：最近 QA；无 QA 404。
- 409：Run 未终态、无 validation-results、结果为空/损坏。
- 结论：全部命令 Passed → Passed；任一失败 → Failed；无命令 → Inconclusive（当前入口拒绝空结果）。

## 3. 安全

- QA 不接收命令/路径，不重跑测试；只读任务 Artifact。
- 输出摘要限长，不采集环境变量/密钥；不改变 Coding Run/Review。
- 与 Review 一样按纯文本展示。

## 4. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **296 通过 / 5 跳过 / 0 失败** |
| QA Service 用例 | ✅ 4（全通过、失败、缺结果、非终态） |
| 前端 lint / build | ✅ 通过 |
| CI | 见对应 run |

### 4.1 独立代码审查\n\nCodex CLI 审查发现 1 个 P1（validation Artifact 未接线导致 QA 不可用）与 2 个 P2（并发 Attempt、UI 无法重跑），全部修复并复测，详见 `docs/reports/AOS-015-CODE-REVIEW.md`。\n\n## 5. 已知限制

1. 只做命令级结果；JUnit XML / Cobertura、用例级结果与覆盖率属于 AOS-015b。
2. 不重新执行 validationCommands，因此 QA 结果反映最近一次 Coding Run 的校验。
3. 无异步队列/取消；创建 QA 同步完成。
4. 真实 Runner 上传 Artifact 未在本地端到端验证；Fake repository 覆盖 QA 逻辑。

## 6. 变更文件

**Runner**：`IValidationCommandRunner.cs`、`ValidationCommandRunner.cs`、`CodexTaskHandler.cs`
（`validation-results.json`）。

**Domain/Infra**：`Domain/Qa/QaRun.cs`、QA 配置、迁移 `AddQaAgent`、`Infrastructure/Qa/QaRepository.cs`。

**Application/API/UI**：`Application/Qa/*`、QA 端点、`QaService` 注册、`CodingRunsView`、`api/qa.js`。

**测试**：`QaServiceTests.cs`。

## 7. 下一步

AOS-015b：JUnit/Cobertura 解析、用例级结果与覆盖率；或 AOS-017 Git Worktree。
