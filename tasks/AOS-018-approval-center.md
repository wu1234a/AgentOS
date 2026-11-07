# AOS-018：Approval Center 审批中心

状态：Completed
优先级：P1
依赖：AOS-013、AOS-012
来源：Kimi Planner 草案 `01a1b5b3-8eaf-7937-ac9e-14f45ea2a65a`（已批准）

## 0. 修订

Kimi 草案含 RBAC 权限、rowversion 并发、目标摘要、Admin 路由；本阶段先交付：

1. 通用 ApprovalRequests、创建/查询/决策 API。
2. 仅记录决策，不改变 CodingRun/PlannerDraft，不自动执行/合并。
3. 使用现有开发 Key 认证，不做细粒度 RBAC；并发由状态检查 + 唯一索引兜底。
4. 前端 `/approvals` 页面。

## 1. 范围

- `ApprovalRequests`：TargetType（CodingRun/PlannerDraft）、TargetId、Status
  （Pending/Approved/Rejected）、申请/决策理由、申请人/决策人（固定 dev-user）、UTC 时间。
- 过滤唯一索引：同一目标仅一个 Pending。
- API：`POST/GET /api/v1/approval-requests`、`GET /{id}`、`POST /{id}/decision`。
- 校验目标存在；拒绝必须填理由；重复决策/重复 Pending → 409。
- 前端列表、发起申请、批准/拒绝。

## 2. 不在范围

- RBAC 权限矩阵、rowversion 乐观并发、目标摘要/快照。
- 自动执行、合并、改变目标状态、通知、多级审批、撤销/重开。

## 3. 验收

- [x] 迁移、创建、重复 Pending 409、拒绝理由必填、批准/拒绝。
- [x] 列表分页/过滤、详情。
- [x] 前端页面构建通过。
- [x] 后端全量测试、CI 通过。
