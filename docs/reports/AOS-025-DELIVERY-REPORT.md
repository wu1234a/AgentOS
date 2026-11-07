# AOS-025 交付报告：合并策略与分支保护集成

**状态：Completed**
**完成时间：2026-11-08**
**任务文件：** `tasks/AOS-025-merge-readiness.md`

## 1. 能力

- `GET /api/v1/merge-readiness?repository=&number=`：
  - PR open、非 draft、mergeable；
  - 审批数 ≥ 配置；
  - `agentos/quality-gate` = success；
  - 可选分支保护 required 检查；
  - 返回 Ready/Blocked 与逐项原因。
- 只读；不自动 merge/approve、不修改保护。

## 2. 配置

```json
"GitHub": {
  "MergePolicies": {
    "owner/repo": {
      "Enabled": true,
      "RequiredApprovals": 1,
      "RequireQualityGate": true,
      "RequireBranchProtection": false
    }
  }
}
```

## 3. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **315 通过 / 5 跳过 / 0 失败** |
| Merge readiness 用例 | ✅ PR + 审批 + 门禁通过 → Ready |
| CI | 见对应 run |

### 3.1 独立代码审查\n\nCodex CLI 审查发现 1 个 P1（mergeable 未知被放行）与 1 个 P2（reviews 未分页），全部修复，详见 `docs/reports/AOS-025-CODE-REVIEW.md`。\n\n## 4. 已知限制

1. 只读评估；没有合并动作/合并队列。
2. 审批计数按 reviewer 最新 review，忽略 CODEOWNERS/团队审批语义。
3. 不检查 PR 冲突详情（仅 mergeable 布尔）。
4. 无 Rulesets 支持。
5. 真实 GitHub API 未在 CI 调用。

## 5. 变更文件

`GitHubPullRequestClient`、`MergePolicyOptions`、`MergeReadinessService`、
`MergeReadinessEndpoints`、GitHubOptions、Program、`SqlMergeReadinessTests.cs`、文档。
