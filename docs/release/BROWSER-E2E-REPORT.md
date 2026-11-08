# 浏览器端到端验收报告

日期：2026-11-09
环境：`http://127.0.0.1:5099`（AgentOS_Prod，个人运行模式）
浏览器：Playwright Chromium

## 结果

```text
14 passed
0 failed
```

## 覆盖范围

| # | 用例 | 结果 |
|---|---|---|
| 1 | Dashboard 加载且无 Console 错误 | ✅ |
| 2 | Projects 加载且无 Console 错误 | ✅ |
| 3 | Usage 加载且无 Console 错误 | ✅ |
| 4 | Workers 加载且无 Console 错误 | ✅ |
| 5 | Pipelines 加载且无 Console 错误 | ✅ |
| 6 | 深链接刷新 | ✅ |
| 7 | 侧边栏导航 | ✅ |
| 8 | 创建项目并停用 | ✅ |
| 9 | Usage 汇总卡片 | ✅ |
| 10 | Pipelines 列表 | ✅ |\n| 11 | Approvals 加载 | ✅ |\n| 12 | Tasks 加载 | ✅ |\n| 13 | Events 加载 | ✅ |\n| 14 | Coding Runs 加载 | ✅ |

## 执行

```powershell
cd E:\Projects\AgentOS\frontend\agentos-web
npm run test:e2e
```

报告：

```text
frontend/agentos-web/playwright-report/index.html
```

失败截图/视频/追踪：

```text
frontend/agentos-web/test-results/
```

## 验收中发现并修复

1. 前端构建时 API Key 来自 `.env.local`，与生产 API Key 不一致，页面请求 401。
   - 新增运行时 `/config.js`，由部署生成，不再把 Key 固化进 bundle。
2. Dashboard 缺少 `data-testid`，补充页面标记。
3. Usage “总 Token”文本存在多个匹配，测试改用 `.first()`。

## 已知限制

1. 仅 Chromium / 桌面 1440×900。
2. 未覆盖审批、任务详情、事件详情、Coding Run 详情。
3. 未接入 CI（需要 API + 数据库环境）。
4. 创建的项目无法删除，测试使用唯一 slug 并停用。
5. 前端仍使用个人 API Key，无登录/多用户权限。
