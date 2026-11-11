# AOS-041：Planner 批量生成 Backlog

状态：Completed
优先级：P1
依赖：AOS-012 Planner、AOS-039 Backlog 自动派发

## 1. 范围

- `POST /api/v1/backlog/generate`：输入一个产品目标，生成 1-10 条 Backlog。
- 使用配置的 Planner API 模型（默认 `kimi-planner`）。
- 模型返回严格 JSON 数组：`kind/title/instruction`。
- `kind` 决定校验命令：
  - backend：build + test
  - frontend：npm ci + build
  - docs：build
  - full：前后端全部
- 生成项自动进入 Pending，由 Backlog Worker 自动派发。
- 输出仍走标准隔离、校验、PR、评审、合并链路。

## 2. 不在范围

- 生成结果的人工审批/暂停后启动。
- 多项目校验模板管理。
- 把生成结果直接写入 Planner 草案表。
- 自动执行无限数量的任务。

## 3. 验收

- [x] 目标可拆成结构化任务。
- [x] 最多 10 条，默认 5 条。
- [x] 非法 JSON/kind/字段被过滤。
- [x] 生成项进入 Pending 并自动派发。
- [x] 生成任务自动合并。
- [x] 构建和测试通过。
