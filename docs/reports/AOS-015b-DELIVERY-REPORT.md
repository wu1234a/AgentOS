# AOS-015b 交付报告：QA 用例级结果与覆盖率

**状态：Completed**
**完成时间：2026-11-07**
**任务文件：** `tasks/AOS-015b-qa-reports.md`

## 1. 能力

- Runner 在 validationCommands 后收集固定位置报告：`junit/*.xml`（≤20 个，单个 ≤2MB）、
  根目录 `coverage.cobertura.xml`（≤4MB），上传为 Artifact。
- QA 读取 Artifact 并解析 JUnit 用例（passed/failed/error/skipped、耗时、失败类型/消息/详情）
  与 Cobertura 行/分支覆盖率。
- `QaTestCases` 持久化；`QaRuns` 增加报告状态、覆盖率、issues。
- QA 面板展示用例通过/失败/错误数与覆盖率；未知覆盖率显示 `—`。

## 2. 数据与 API

- 迁移 `AddQaReports`：`QaTestCases` + `QaRuns` 新列。
- `GET /api/v1/coding-runs/{id}/qa-runs/latest` 响应新增报告状态、覆盖率与用例计数；
  命令结果保持兼容。
- 不新增上传端点；Runner 复用 Artifact 通道。

## 3. 安全

- XmlReader `DtdProcessing=Prohibit`、`XmlResolver=null`、文档大小限制，阻止 XXE/DTD。
- 报告路径固定，仅 `junit/` 一层和根 coverage 文件；不读取任意路径。
- 单文件/文件数/用例数限制；解析失败只记录安全 issue，不保存原始 XML。
- QA 不重跑测试、不修改代码/Review/Coding Run。

## 4. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **300 通过 / 5 跳过 / 0 失败** |
| 解析器用例 | ✅ 4（JUnit 状态、Cobertura、DTD/XXE 拒绝、无报告） |
| QA Service 用例 | ✅ 4（沿用 AOS-015） |
| 前端 lint/build | ✅ 通过 |
| CI | 见对应 run |

## 5. 已知限制

1. 只解析固定位置报告；测试框架需按此约定输出。
2. 不解析包/类/文件级覆盖率与历史趋势。
3. 单文件损坏时其他报告仍处理，状态 partial；不保留原始 XML。
4. 真实 Runner 报告上传未在本地端到端跑；解析器与 QA 逻辑有测试覆盖。
5. 用例详情 API/分页未单独提供；当前 QA 响应只含计数（详情表已持久化，后续可加查询）。

## 6. 下一步

AOS-017 Git Worktree 隔离，或补充 QA 用例分页 API/UI。
