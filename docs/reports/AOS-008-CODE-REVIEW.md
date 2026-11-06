# AOS-008 独立代码审查报告

**审查对象：** commit `2d03fa5`（AOS-008 可观测性与 Artifact），后续修复 `e677dc3`、`a848a10`
**审查方式：** Codex CLI `codex exec review`
**结论：** 1 个 P1（缺失源文件导致干净检出无法编译）与 6 个 P2，全部修复。

## 1. 发现与处理

| # | 级别 | 问题 | 处理 |
|---|---|---|---|
| 1 | P1 | `Artifact.cs` / `ArtifactStore.cs` 未进入 Git（根目录 `artifacts/` 忽略规则在 Windows 大小写不敏感下误伤源码目录），干净检出无法编译 | `.gitignore` 改为仅忽略根目录 `/artifacts/`，补提交两个源文件；CI 已通过 |
| 2 | P2 | Task 级日志分页仅用 `Sequence`，多 Attempt 时序号重复会跳过日志 | 移除 Task 级分页接口，前端按 **Attempt** 选择并分页（`(AttemptId, Sequence)` 唯一），彻底消除歧义 |
| 3 | P2 | Kestrel 全局请求体上限为 `Events:MaxRequestBodyBytes`（256KB），Artifact 上传 10MB 会被 413 | 上传端点读取前把 `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize` 提升到 `Artifacts:MaxBytes`，仍由存储层做流式大小校验 |
| 4 | P2 | 仅成功执行上传 artifact；失败/超时/取消的 stdout/stderr 丢失 | `CodexTaskHandler` 在所有终态（Cancelled/TimedOut/Failed/Succeeded）附带可用的 artifact |
| 5 | P2 | 前端 `new Date(offset-free)` 把 SQL `datetime2` 当本地时间 | `formatUtc` 对无时区值补 `Z` 再按本地展示；新增说明 |
| 6 | P2 | Runner 崩溃后 `Status` 仍为 Online，页面长期显示在线 | Worker 列表按 2 分钟心跳新鲜度推导为 Offline，Disabled 保持不变 |
| 7 | P2 | `DATEDIFF(millisecond)` 超过约 24.9 天会溢出导致 Dashboard 500 | 平均值改用 `DATEDIFF(second)`，避免整数溢出 |

## 2. 修复后验证

- 后端全量测试：**250 通过 / 4 跳过 / 0 失败**。
- 前端 lint / build：通过。
- CI：run#18 通过；最终修复提交后再次确认。

## 3. 审查未覆盖项

- 审查为只读环境，未独立执行测试；以上为修复后本地实际结果。
- 任务级日志分页接口已移除，日志按 Attempt 分页（符合“查看每次 Attempt 的日志”验收项）。
