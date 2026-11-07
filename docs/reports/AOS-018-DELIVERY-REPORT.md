# AOS-018 交付报告：Approval Center

**状态：Completed**
**完成时间：2026-11-07**
**任务文件：** `tasks/AOS-018-approval-center.md`

## 1. 能力

- `ApprovalRequests`（迁移 `AddApprovalRequests`）：CodingRun/PlannerDraft 审批。
- `POST/GET /api/v1/approval-requests`、`GET /{id}`、`POST /{id}/decision`。
- 同一目标仅一个 Pending；拒绝必须填理由；重复决策 409。
- 审批仅记录决策，不执行、不合并、不改变目标状态。
- `/approvals` 页面：列表、发起、批准/拒绝。

## 2. 安全

- 现有开发 Key 认证；申请人/决策人由服务端固定为 `dev-user`（后续接 RBAC）。
- 目标存在性校验；理由长度限制；纯文本展示。

## 3. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **307 通过 / 5 跳过 / 0 失败** |
| 审批用例 | ✅ 创建/重复 409/拒绝理由/批准 |
| 前端 lint/build | ✅ 通过 |
| CI | 见对应 run |

### 3.1 独立代码审查\n\nCodex CLI 审查发现 1 个 P1（并发决策覆盖）与 3 个 P2（并发创建 500、GUID 格式、前端分页），全部修复，详见 `docs/reports/AOS-018-CODE-REVIEW.md`。\n\n## 4. 已知限制

1. 无细粒度 RBAC，仅有开发 Key。
2. 无 rowversion 乐观并发；并发决策依赖状态检查。
3. 目标摘要/内容快照、通知、多级审批、撤销未实现。
4. 与 PlannerDraft 已有 Approve/Reject 不自动同步。

## 5. 变更文件

`ApprovalRequest.cs`、配置、迁移、`ApprovalContracts/Service`、`ApprovalRepository`、
`ApprovalEndpoints`、DI/Program、`ApprovalsView.vue`/`api/approvals.js`、路由/菜单、
`SqlApprovalTests.cs`、文档。
