# AgentOS 文档索引

- 当前代码：`2b7ab51`
- 更新时间：2026-11-14

## 面向使用

- [使用教程](USER-GUIDE.md) — 环境、启动、界面、立项、Backlog、模型、排障
- [本地部署](deployment-local.md)
- [GitHub 配置](GITHUB-CONFIG.md)

## 目标形态与改进路线

- [目标项目运营模型](PROJECT-OPERATING-MODEL.md) — 最终项目流程、角色、文档体系、分阶段改进路线

## 面向开发

- [技术文档](TECHNICAL-GUIDE.md) — 架构、数据模型、流程、API、安全、限制
- [架构](ARCHITECTURE.md)
- [数据库 ER](DATABASE-ER.md)
- [事件规范](EVENT-SPECIFICATION.md)
- [安全与运维](SECURITY-AND-OPERATIONS.md)
- [多模型](MULTI-MODEL.md)
- [Codex 实现指南](CODEX-IMPLEMENTATION-GUIDE.md)

## 交付报告

- [AOS-044 全自动化硬化](reports/AOS-044-DELIVERY-REPORT.md)
- [AOS-045 多 Agent 立项](reports/AOS-045-DELIVERY-REPORT.md)
- [AOS-046 立项后续控制](reports/AOS-046-DELIVERY-REPORT.md)
- [AOS-047 预算/版本/审批](reports/AOS-047-DELIVERY-REPORT.md)
- [AOS-048 子集/费用/依赖](reports/AOS-048-DELIVERY-REPORT.md)
- [AOS-049 版本历史与审计](reports/AOS-049-DELIVERY-REPORT.md)
- [AOS-050 Backlog 依赖](reports/AOS-050-DELIVERY-REPORT.md)
- [AOS-051 依赖图与对比](reports/AOS-051-DELIVERY-REPORT.md)
- [AOS-052 生成校验与模型价格](reports/AOS-052-DELIVERY-REPORT.md)

## 当前结论

```text
任务级自动化：已实现
项目立项自动化：已实现
多 Agent 规划：已实现
依赖顺序执行：已实现
完全无人值守：尚未实现
```

主要未完成项：

```text
真实登录身份 / RBAC
真实文本冲突自动解决
依赖失败自动重排
整机重启验证
```
