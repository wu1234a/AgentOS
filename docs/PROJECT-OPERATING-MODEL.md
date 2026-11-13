# AgentOS 目标项目运营模型与改进路线

- 文档状态：**方案评审稿**
- 更新时间：2026-11-14
- 当前代码：`2b7ab51`
- 本文不包含代码修改
- 目标：定义“最终完成后，一个新项目如何从目标走到验收”，并给出分阶段改进路线

---

## 1. 结论

AgentOS 的最终形态不应该是：

```text
一个会写代码的超级 Agent
```

也不应该是：

```text
一个无限讨论的多 Agent 群聊
```

而应该是：

```text
目标驱动的项目操作系统
+ 可审计的执行工厂
```

最终完成后，人和系统的分工是：

```text
人：
  提出目标
  给出边界和取舍
  批准关键方案
  验收最终结果

AgentOS：
  组织讨论
  形成文档
  拆解任务
  分配角色
  执行开发
  验证质量
  跟踪完成度
  提供证据
```

核心原则：

1. **项目是主体，任务只是执行单元。**
2. **文档是共享上下文，不是装饰。**
3. **角色有明确职责，不是自由认领。**
4. **讨论有边界，不无限辩论。**
5. **关键节点必须有人批准。**
6. **完成以验收标准为准，不以“任务跑完”为准。**

---

## 2. 目标形态：五层模型

```text
┌──────────────────────────────────────────────┐
│ 5. 验收层                                     │
│ 目标完成度 / 验收证据 / 人工签收 / 项目关闭    │
├──────────────────────────────────────────────┤
│ 4. 执行层                                     │
│ 角色队列 / Coder / Reviewer / QA / Security    │
├──────────────────────────────────────────────┤
│ 3. 计划层                                     │
│ Epic / Task / 依赖 / 任务文档 / 验收标准       │
├──────────────────────────────────────────────┤
│ 2. 文档层                                     │
│ VISION / ROADMAP / ARCHITECTURE / DECISIONS    │
├──────────────────────────────────────────────┤
│ 1. 项目层                                     │
│ 项目目标 / 简报 / 约束 / 成功标准              │
└──────────────────────────────────────────────┘
```

现有 AgentOS 已经具备第 4 层的大部分能力，第 1、2、3、5 层需要补齐。

---

## 3. 最终完成后的新项目流程

下面以一个真实例子说明：

> **目标：为 3dERP 增加轻量报表模块，先做库存报表，再扩展采购/销售。**

### 阶段 0：立项入口

人可以有两种方式：

#### 方式 A：人工创建项目

```text
Projects → 新建项目
填写：
  项目名称
  slug
  一句话目标
  详细简报
  个人想法/备注
  成功标准
  仓库地址
```

#### 方式 B：从目标自动建议项目

```text
人只输入：
  “我想给 3dERP 增加报表模块，先做库存，预算有限，尽量复用现有模块。”

AgentOS：
  → 识别项目名/slug/仓库
  → 生成项目简报草案
  → 给出约束和风险
  → 人工确认创建
```

系统不自动创建未经确认的项目。

**产物：**

```text
Project
  Goal
  Brief
  PersonalNotes
  AcceptanceCriteria
  Repository
  Status = Draft
```

**人工门禁 G1：项目章程确认**

---

### 阶段 1：项目章程

系统根据目标和笔记生成 `PROJECT.md`：

```markdown
# 3dERP 报表模块

## 目标
为 3dERP 增加只读报表能力，首期覆盖库存。

## 成功标准
- 库存报表可按仓库/物料筛选
- 复用现有权限过滤
- 不破坏现有 API
- 有集成测试
- 可人工验收

## 约束
- .NET 10 + EF Core + Vue 3
- 预算有限
- 不做复杂 BI
- 不引入第三方报表平台

## 非目标
- 导出
- 缓存
- 采购/销售首期不做

## 风险
- 指标口径未冻结
- 数据量未知
- 权限过滤必须统一
```

人可以直接编辑。

**状态：**

```text
Project.Status = CharterReview
→ 人工批准
→ Project.Status = Planning
```

---

### 阶段 2：结构化头脑风暴

不是无限群聊，而是有限轮次。

参与角色：

```text
Product Strategist   产品价值、范围、优先级
Architecture         技术方案、依赖、可维护性
Cost/Risk            成本、周期、风险、性价比
Delivery             交付节奏、拆分、依赖
Red Team             专门挑漏洞
Chair                汇总，不做实现
```

轮次：

```text
Round 1：独立提案
Round 2：交叉评审
Round 3：Chair 综合
```

每轮都有 Token/时间/输出上限。

**产物：**

```text
BRAINSTORM.md
  三份提案
  三份评审
  推荐方案
  被否方案及原因
  关键取舍
```

**人工门禁 G2：方案方向确认**

人可以：

- 接受
- 拒绝
- 写修订意见，触发下一轮

---

### 阶段 3：最终计划文档

Chair 输出结构化计划，系统落成正式文档：

```text
docs/projects/3derp-reports/
  PROJECT.md
  BRAINSTORM.md
  ROADMAP.md
  ARCHITECTURE.md
  ACCEPTANCE.md
  DECISIONS/
    ADR-001-read-only-report.md
  TASKS/
    T-001.md
    T-002.md
```

`ROADMAP.md`：

```markdown
# 路线图

## M1 盘点与口径
- 技术盘点
- 指标口径冻结
- 权限过滤确认

## M2 库存报表后端
- 查询服务
- 只读 API
- 集成测试

## M3 库存报表前端
- 页面
- 筛选
- 联调

## M4 采购/销售扩展
- 口径冻结
- API
- 页面
```

`ARCHITECTURE.md`：

```markdown
# 技术方案

## 模块边界
## API 设计
## 数据来源
## 权限过滤
## 性能策略
## 失败处理
```

`ACCEPTANCE.md`：

```markdown
# 验收标准

- [ ] 库存报表 API 可用
- [ ] 权限过滤覆盖
- [ ] 前端页面可操作
- [ ] 测试通过
- [ ] 人工验收通过
```

**人工门禁 G3：最终计划批准**

```text
Plan.Status = PendingReview
→ 人批准
→ Plan.Status = Approved
→ Project.Status = Executing
```

---

### 阶段 4：工作分解

Architect/Planner 根据 `ROADMAP.md` 生成 Epic 和 Task。

每个 Task 必须有任务文档：

```markdown
# T-002 新增库存报表只读 API

## 所属
M2 / 库存报表后端

## 背景
...

## 目标
...

## 技术方案
...

## 依赖
T-001

## 验收标准
- [ ] GET /api/reports/inventory 返回汇总
- [ ] 支持分页和筛选
- [ ] 权限过滤
- [ ] 集成测试通过

## 交付物
- API
- DTO
- 测试
- 文档更新

## 风险
...
```

任务字段：

```text
TaskKey
EpicKey
Title
RequiredRole
DependsOn
TaskDocumentPath
AcceptanceCriteria
RiskLevel
Status
```

**状态：**

```text
Draft → Ready → InProgress → InReview → Verified → Done
```

---

### 阶段 5：角色化执行

不是“谁抢到谁做”，而是按角色进入队列。

#### A：Architect / Planner

负责：

- 拆分任务
- 规划路线
- 技术方案
- 输出/更新任务文档
- 不直接写业务代码

#### B：Coder

负责：

- 阅读任务文档
- 实现代码
- 补充测试
- 更新实现记录
- 开 PR

Coder 完成后必须：

```text
更新 T-002.md 的“实现记录”
附上测试命令和结果
PR 描述引用 T-002
```

#### C：Reviewer

负责：

- 代码正确性
- 可维护性
- 与任务文档一致性
- 发现 Critical/Major 阻断

#### D：Security

按风险触发：

- 认证/授权
- 输入校验
- 密钥泄露
- 依赖风险
- SQL 注入/越权

#### E：Performance

按风险触发：

- 查询计划
- N+1
- 分页
- 大数据量
- 缓存策略

#### F：QA

负责：

- 测试证据
- 验收标准核对
- 回归
- 不接受“代码能跑”作为完成

#### G：Tech Writer

负责：

- 文档一致性
- 计划/任务/验收文档更新
- 防止文档漂移

#### H：Chair / Coordinator

负责：

- 汇总
- 冲突协调
- 推动下一步
- 不直接实现

**人工门禁 G4：高风险任务/生产变更**

---

### 阶段 6：质量门

每个 Task 完成前必须通过：

```text
代码构建
测试
Review
安全（按风险）
性能（按风险）
文档更新
验收标准核对
```

PR 必须包含：

```text
TaskKey
任务文档链接
测试证据
风险说明
```

Review 不只看 diff，还检查：

```text
是否偏离任务文档
是否偏离项目目标
是否引入非目标范围
```

---

### 阶段 7：里程碑验收

每个里程碑完成：

```text
M2 库存报表后端
→ 所有 Task Verified
→ QA 汇总证据
→ 更新 ROADMAP.md
→ 人工确认里程碑
```

不是“任务跑完自动进入下一阶段”。

**状态：**

```text
Project.Milestone M2 = Verified
```

---

### 阶段 8：项目完成与人工验收

当所有里程碑完成：

```text
Project.Status = Acceptance
```

系统生成 `ACCEPTANCE-REPORT.md`：

```markdown
# 项目验收报告

## 目标
## 交付内容
## 里程碑结果
## 测试与质量证据
## 未完成/延期项
## 风险与遗留
## 结论建议
```

人做最终验收：

```text
通过 → Project.Status = Completed
不通过 → Project.Status = Rework
放弃 → Project.Status = Cancelled
```

最终验收不是形式，必须能回答：

- 目标是否达成？
- 成功标准是否满足？
- 是否有未说明的范围蔓延？
- 是否可以维护？
- 是否值得上线？

**人工门禁 G5：最终验收**

---

## 4. 最终状态机

### 项目

```text
Draft
→ CharterReview
→ Planning
→ PlanReview
→ Approved
→ Executing
→ MilestoneReview
→ Acceptance
→ Completed

异常：
OnHold
Rework
Cancelled
```

### 计划

```text
Queued
→ Generating
→ PendingReview
→ Approved
→ Rejected
→ Failed
```

### 任务

```text
Draft
→ Ready
→ InProgress
→ InReview
→ Verified
→ Done

异常：
Blocked
NeedsAttention
Cancelled
```

### 文档

```text
Draft
→ InReview
→ Approved
→ Superseded
```

---

## 5. 角色职责矩阵

| 角色 | 主要职责 | 可写代码 | 可批准 |
|---|---|---:|---:|
| Human Sponsor | 目标、边界、验收 | 否 | 是 |
| Product | 范围/价值/优先级 | 否 | 否 |
| Architect | 方案/任务文档 | 少量 | 技术方案 |
| Planner | 拆解/依赖 | 否 | 否 |
| Coder | 实现/测试/文档回写 | 是 | 否 |
| Reviewer | 代码审查 | 否 | 代码质量 |
| Security | 安全审查 | 否 | 安全门 |
| Performance | 性能审查 | 否 | 性能门 |
| QA | 验收证据 | 可写测试 | 质量门 |
| Tech Writer | 文档一致性 | 否 | 文档门 |
| Chair | 汇总/推进 | 否 | 否 |
| AgentOS | 执行/调度/审计 | 否 | 否 |

---

## 6. 文档体系

### 6.1 项目级

```text
PROJECT.md          项目章程
BRAINSTORM.md       讨论记录
ROADMAP.md          路线图
ARCHITECTURE.md     技术方案
ACCEPTANCE.md       验收标准
ACCEPTANCE-REPORT.md 验收报告
```

### 6.2 决策级

```text
DECISIONS/ADR-001-xxx.md
```

记录：

```text
背景
选项
决定
理由
影响
```

### 6.3 任务级

```text
TASKS/T-001.md
```

记录：

```text
背景
目标
方案
依赖
验收标准
实现记录
测试证据
审查结论
```

### 6.4 证据级

```text
EVIDENCE/T-001/
  test-output.txt
  review.json
  perf.md
  security.md
```

---

## 7. 系统如何保证闭环

### 7.1 文档不是附件

任务必须引用文档：

```text
BacklogItem.TaskDocumentPath
ProjectPlan.DocumentRoot
```

没有文档的任务不能进入 Ready。

### 7.2 文档不是一次性

Coder 完成任务必须更新：

```text
实现记录
测试证据
```

Reviewer/QA 必须更新：

```text
审查结论
验收结论
```

### 7.3 完成不是状态

任务 Done 需要：

```text
代码合并
测试通过
审查通过
文档更新
验收标准核对
```

缺一不可。

### 7.4 项目完成不是任务清零

项目 Completed 需要：

```text
所有里程碑 Verified
验收报告生成
人工签收
```

---

## 8. 当前 AgentOS 差距

| 能力 | 当前 | 目标 |
|---|---|---|
| 项目目标 | 无 | Project.Goal/Brief |
| 自动建项目 | 无 | 目标→建议→确认 |
| 正式文档 | JSON | Markdown + 版本 |
| 头脑风暴 | 固定 3 角色 | 4-6 角色/有限轮次 |
| 角色分工 | coder/reviewer | 角色队列 |
| 任务文档 | instruction | TaskDocument |
| 文档回写 | 无 | Coder/Reviewer 回写 |
| 安全/性能 | 单一 Review | 专项 Agent |
| 里程碑验收 | Epic 计数 | Milestone Verified |
| 项目完成 | 无 | Acceptance + Sign-off |
| 身份 | Actor header | 登录/RBAC |

---

## 9. 改进路线

### Phase 0：本方案评审

- 输出：本文档
- 人工确认目标流程和范围
- 不写代码

**退出条件：** 你批准本路线。

---

### Phase 1：项目目标与项目空间

目标：让项目第一次拥有“灵魂”。

实现：

- Project 增加：

```text
Goal
Brief
PersonalNotes
AcceptanceCriteria
Repository
Status
```

- 项目创建页支持目标和简报
- 从目标自动建议项目
- 项目详情页
- 项目状态机
- 保留旧项目兼容

**产物：**

```text
Project 详情
Project Charter v1
```

**验收：**

- 能从目标创建项目
- 能编辑目标和简报
- 能人工确认项目章程
- 旧项目不受影响

---

### Phase 2：正式文档与计划

目标：让计划和路线成为可版本化文档。

实现：

- ProjectDocument 表
- Markdown 渲染
- 计划批准时生成：

```text
PROJECT.md
BRAINSTORM.md
ROADMAP.md
ARCHITECTURE.md
ACCEPTANCE.md
```

- 文档版本
- 文档与 Plan 关联
- 前端文档查看/对比

**验收：**

- 批准的 Plan 自动生成文档
- 文档可查看、可版本对比
- 计划修订生成新文档版本

---

### Phase 3：任务文档与角色队列

目标：从“任务指令”升级为“任务文档 + 角色”。

实现：

- Task 增加：

```text
RequiredRole
TaskDocumentPath
AcceptanceCriteria
RiskLevel
```

- 任务文档模板
- Architect 产出任务文档
- Coder 只领取 RequiredRole=coder
- Reviewer/Security/Performance/QA 独立角色
- 文档回写
- 角色队列

**验收：**

- 一个任务有正式文档
- Coder 完成时更新实现记录
- Reviewer 审查引用任务文档
- 角色不能越权领取

---

### Phase 4：质量门与证据

目标：让“完成”有证据。

实现：

- Review findings 落证据
- 安全审查清单
- 性能审查清单
- QA 验收标准核对
- PR 必须引用 TaskKey/文档
- 未满足验收标准不能 Done

**验收：**

- 每个 Done 任务都有测试/审查/文档证据
- Critical/Major 阻断
- 高风险任务必须安全/性能审查

---

### Phase 5：里程碑与项目验收

目标：项目级闭环。

实现：

- Milestone 实体
- 里程碑状态
- 目标完成度
- 验收报告生成
- 人工最终签收
- 项目 Completed/Archived

**验收：**

- 里程碑必须人工确认
- 项目完成必须人工验收
- 验收报告包含目标/证据/遗留

---

### Phase 6：可靠性硬化

目标：从“能用”到“可长期运行”。

实现：

- 真实登录/RBAC
- 真实 Git 冲突解决
- 依赖失败自动重排
- 多实例合并锁
- 整机重启验证
- 备份/恢复演练

**验收：**

- 身份不可伪造
- 冲突可恢复
- 依赖失败可重排
- 重启后自动恢复

---

## 10. 路线优先级

```text
P0  Phase 1 项目目标
P0  Phase 2 正式文档
P1  Phase 3 角色队列
P1  Phase 4 质量证据
P1  Phase 5 项目验收
P2  Phase 6 可靠性
```

不建议跳过 Phase 1/2 直接做 Phase 3，否则角色没有共同上下文。

---

## 11. 非目标

本路线明确不做：

- 7 个 Agent 无限群聊
- 全自动决定产品方向
- 全自动生产发布
- 自动支付/采购
- 自动删除生产数据
- 无人工验收的“完成”
- 以文档数量冒充质量

---

## 12. 风险

| 风险 | 应对 |
|---|---|
| 文档漂移 | 文档与任务/PR 强关联 |
| 角色重叠 | RequiredRole + 权限矩阵 |
| 讨论成本高 | 限轮次/限 Token |
| 假完成 | 证据门 + 人工验收 |
| 过度流程 | 小任务走轻量路径 |
| 身份缺失 | Phase 6 前不开放公网 |
| 自动改坏代码 | worktree + 校验 + PR + review |

---

## 13. 成功指标

```text
项目级：
  计划到首个 PR 时间
  项目周期
  人工干预次数
  返工率
  验收一次通过率

任务级：
  任务成功率
  Review 阻断率
  测试失败率
  文档更新率
  单任务成本

系统级：
  Runner 在线率
  任务平均等待时间
  模型回退率
  合并失败率
```

---

## 14. 示例：3dERP 报表项目最终流程

```text
1. 人说：
   “给 3dERP 加报表，先库存，预算有限。”

2. AgentOS 建议项目：
   3derp-reports / 仓库 wu1234a/3dERP

3. 人生成/确认 PROJECT.md

4. 3 个分析 Agent + Red Team 讨论
   → BRAINSTORM.md

5. Chair 输出：
   ROADMAP.md
   ARCHITECTURE.md
   ACCEPTANCE.md

6. 人批准计划

7. Architect 生成：
   T-001 盘点与口径.md
   T-002 库存报表 API.md
   T-003 库存报表页面.md

8. Coder 领取 T-002
   → 实现
   → 测试
   → 更新 T-002 实现记录
   → PR

9. Reviewer 审查
   → 通过

10. QA 核对验收标准
    → 证据

11. M2 里程碑人工确认

12. 全部里程碑完成
    → ACCEPTANCE-REPORT.md
    → 人验收
    → Project Completed
```

---

## 15. 关键设计决策

### 为什么不是自由多 Agent 群聊？

因为：

```text
成本不可控
结论不稳定
责任不清
容易产生文档垃圾
```

更好的是：

```text
固定角色
固定轮次
结构化输出
Chair 综合
人批准
```

### 为什么文档优先？

因为项目不是一次任务。

文档提供：

```text
共享上下文
可审计决策
任务依据
验收依据
```

### 为什么保留现有 AgentOS？

因为它已经解决了最难的部分：

```text
隔离执行
任务队列
校验
PR
评审
合并
失败恢复
```

这些不应重写，只需要在上面加项目层。

---

## 16. 下一步

请先评审本文档，确认：

1. 最终流程是否符合你的预期
2. 角色是否过多/过少
3. 文档体系是否可接受
4. Phase 1 范围是否合适
5. 是否先做 Phase 1，还是调整顺序

确认后，下一步进入：

> **Phase 1：项目目标与项目空间**
> 先让 Project 拥有 Goal/Brief/AcceptanceCriteria/Status，并支持从目标创建项目。

在获得确认前，不修改代码。
