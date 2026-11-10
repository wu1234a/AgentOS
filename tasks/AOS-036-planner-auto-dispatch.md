# AOS-036：Planner 批准后自动派发任务

状态：Completed
优先级：P1
依赖：AOS-012 Planner、AOS-002 Event Hub、AOS-004 Rules、Runner delivery

## 1. 范围

- Planner 草案批准后，可配置地自动创建标准 AgentOS 事件。
- 事件走现有 Rules → Task → Runner 链路，不新增旁路。
- 自动派发携带：
  - 草案标题、目标、完整 Markdown 计划；
  - 校验命令；
  - delivery 配置（仓库、基础分支、token 环境变量）；
  - 由草案 ID 生成的唯一分支 `aos/planner-{draftId:N}`。
- 默认关闭；只有配置的项目 slug 才允许派发。
- 审批响应通过 `X-AgentOS-Dispatch*` 头返回派发结果。

## 2. 不在范围

- 自动 Review、Quality Gate 和自动合并。
- Planner 审批 UI 的派发结果展示。
- 派发失败后的自动重试/补派接口。
- 多项目任务模板管理 UI。

## 3. 验收

- [x] 配置关闭时不创建事件。
- [x] 项目 slug 不匹配时跳过。
- [x] delivery 仓库缺失时拒绝派发。
- [x] 批准后创建事件并生成任务。
- [x] 重复审批不会重复派发。
- [x] AgentOS 全量测试通过。
