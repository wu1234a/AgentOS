# Codex 实施 AgentOS 的统一开发约定

本文件是所有 AOS 开发任务的上位约束。执行任何 `tasks/AOS-xxx-*.md`
前，必须先阅读本文件。

## 1. 工作顺序

1.  阅读
    README、ROADMAP、ARCHITECTURE、DATABASE-ER、EVENT-SPECIFICATION、SECURITY-AND-OPERATIONS。
2.  阅读当前唯一指定的 AOS 任务文件。
3.  检查 Git 状态、当前分支、仓库目录和已有实现。
4.  输出简短实施计划、可能影响的文件和数据库变化。
5.  等待用户确认后开始执行（如果用户明确授权连续执行整个任务，可直接开始）。
6.  按任务范围实现。
7.  执行测试和质量检查。
8.  更新文档和任务状态。
9.  输出交付报告。

## 2. 范围控制

- 一次只执行一个 AOS 任务。
- 不得提前实现后续任务的完整功能。
- 可以创建必要接口或占位抽象，但必须标记为未实现，不能伪称已完成。
- 不得擅自改变架构基线。
- 如果确需改变，先创建 ADR 提案并说明影响。

## 3. 前端 JavaScript 规则

- Vue 3 + Vite。
- 使用 JavaScript，不使用 TypeScript。
- 不创建 `.ts`、`.tsx`。
- Vue SFC 使用 `<script setup>`，不使用 `lang="ts"`。
- 使用 Pinia 管理跨页面状态。
- Axios 统一封装。
- 页面组件不直接重复定义 API URL。
- ESLint 必须能检查 JS 和 Vue 文件。
- 不引入 TypeScript 类型声明文件作为替代方案。
- 对复杂对象可用 JSDoc，但不能把项目转成 TS。

## 4. 后端规则

- 使用 .NET 10。
- Minimal API 路由按功能分组。
- 业务规则放在 Application/Domain，不堆在 Endpoint lambda。
- EF Core 查询必须注意异步、取消令牌和分页。
- 数据库写操作必须明确事务边界。
- 不返回 EF Entity 作为公开 API 响应，使用 DTO。
- 错误响应使用统一 ProblemDetails 风格。
- 所有时间统一使用 UTC。
- 日志使用结构化字段，不拼接敏感信息。

## 5. 数据库规则

- 使用 EF Core Migration。
- 不允许 `EnsureDeleted`、自动 Drop、自动重建生产数据库。
- 不允许为了通过测试删除旧 Migration 或测试。
- 迁移必须有升级测试。
- 需要破坏性变更时先提出 Expand-and-Contract 方案。
- 并发领取必须用真实 SQL Server 测试。
- 唯一约束和外键由数据库兜底。

## 6. Agent 执行规则

- 不把任意文本作为 shell 命令执行。
- 不在 API 请求线程中运行长任务。
- 任务执行必须有超时、取消和退出码。
- Runner 必须验证项目和工作区白名单。
- 所有进程输出都要脱敏。
- Codex Adapter 不得默认绕过沙箱或审批。
- 未经批准不得执行生产操作。

## 7. 测试要求

每项任务必须包含： - Domain/Application 单元测试（适用时）。 - API 或
Infrastructure 集成测试（适用时）。 - 正常路径测试。 - 失败路径测试。 -
权限或输入校验测试。 - 并发/幂等测试（适用时）。 - 回归测试。

不得用“代码看起来正确”代替测试。

## 8. 交付报告格式

每个任务完成后，在 `docs/reports/` 创建：

`AOS-xxx-DELIVERY-REPORT.md`

至少包含： - 任务目标。 - 实现摘要。 - 修改文件清单。 - 数据库
Migration。 - API 变化。 - 执行的测试命令。 -
测试结果（Pass/Fail/Skipped）。 - 安全检查。 - 已知限制。 -
未完成事项。 - 下一任务建议。

只有验收通过后，才能将 ROADMAP 中该任务标记为 Completed。
