# AOS-025：合并策略与分支保护集成

状态：Completed
优先级：P1
依赖：AOS-022、AOS-023、AOS-024b
来源：Kimi Planner 草案 `01a1b70b-ffb8-7e37-bd85-7326a156c19f`（已批准）

## 0. 修订

Kimi 草案含自动合并、规则引擎、冲突处理。本阶段只做**只读合并就绪评估**：

1. PR 状态/草稿/mergeable。
2. 必需审批数。
3. 质量门禁状态。
4. 可选分支保护 required check。
5. 不自动 merge/approve、不修改保护。

## 1. 范围

- `GitHub:MergePolicies`：每仓库 `Enabled/RequiredApprovals/RequireQualityGate/RequireBranchProtection`。
- GitHub client 新增：PR merge info、reviews 审批计数、combined status。
- `MergeReadinessService`：逐项 checks（pr-open/not-draft/mergeable/approvals/quality-gate/branch-protection），全部通过才 Ready。
- API：`GET /api/v1/merge-readiness?repository=&number=`。
- 无数据库迁移（读取 QualityGateEvaluations）。

## 2. 不在范围

- 自动 merge/approve、rebase、冲突解决、修改分支保护。
- 合并队列、规则引擎、GitHub Rulesets。
- 前端。

## 3. 安全

- 仓库必须在 MergePolicies；开发 Key 认证。
- Token 只读 PR/status/保护；无 merge 调用。
- 审批计数按每个 reviewer 最新状态。
- 质量门禁必须 success，缺失/失败阻断。

## 4. 验收

- [x] PR/审批/门禁全部满足 → Ready。
- [x] 逐项 checks 返回原因。
- [x] 不调用 merge/approve。
- [x] 后端全量测试、CI 通过。
