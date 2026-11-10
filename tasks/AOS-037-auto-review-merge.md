# AOS-037：PR 自动评审与自动合并

状态：Completed
优先级：P1
依赖：AOS-025 合并策略、AOS-036 Planner 自动派发、GitHub token

## 1. 范围

- AgentOS 自动发现 `aos/` 前缀的开放 PR。
- 为尚未评审的 head SHA 自动排队独立评审。
- 评审只允许 `Critical` / `Major` 阻塞合并。
- 评审通过、PR mergeable、目标分支匹配时自动 squash merge。
- 不依赖 GitHub Webhook；轮询即可工作。
- 支持按仓库启用/关闭和配置轮询间隔。

## 2. 不在范围

- 人工审批卡片。
- GitHub Actions quality gate 的接入。
- 自动 rebase/冲突解决。
- 自动合并非 `aos/` 分支。
- 多仓库并发调度优化。

## 3. 验收

- [x] 自动评审 AgentOS PR。
- [x] `Info` / `Minor` 不阻塞合并。
- [x] `Critical` / `Major` 阻塞合并。
- [x] 自动 squash merge。
- [x] 端到端 smoke PR 自动合并。
- [x] AgentOS 全量测试通过。
