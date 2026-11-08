# 前端管理页：Projects / Usage

状态：Completed
来源：已批准后续建议

## 范围

- Projects 页面：列表、注册、编辑名称/并发配额、启用/停用。
- Usage 页面：默认最近 30 天，按项目/日期/模型展示 Token 与估算费用、缺失价格分组。
- 路由 `/projects`、`/usage`，侧边栏入口。
- 前端 Vue 3 + JavaScript，无 TypeScript。

## 验证

- `npm run lint`：通过。
- `npm run build`：通过。
- 后端无变更；全量测试沿用 **330 通过 / 5 跳过**。

## 已知限制

1. Projects 页未展示运行任务数。
2. Usage 页无图表/CSV 导出。
3. 前端使用开发 Key（沿用现有 http 配置）。
