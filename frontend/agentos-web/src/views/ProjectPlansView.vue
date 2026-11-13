<script setup>
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { listProjects } from '../api/projects'
import {
  approveProjectPlan,
  createProjectPlan,
  getProjectPlan,
  getProjectPlanProgress,
  listProjectPlans,
  rejectProjectPlan,
  releaseProjectPlan,
  reviseProjectPlan
} from '../api/projectPlans'
import { formatUtc } from '../utils/format'

const items = ref([])
const projects = ref([])
const loading = ref(false)
const error = ref('')
const statusFilter = ref('')
const createVisible = ref(false)
const detailVisible = ref(false)
const selected = ref(null)
const detailLoading = ref(false)
const activeTab = ref('plan')
const progress = ref(null)
const feedback = ref('')
const releasing = ref(false)
const revising = ref(false)

const form = reactive({
  projectId: '',
  title: '',
  goal: '',
  constraints: '',
  repository: 'wu1234a/3dERP',
  baseBranch: 'main',
  remote: 'origin',
  tokenEnvironmentVariable: 'AGENTOS_GITHUB_TOKEN',
  requirePullRequest: true
})

let pollTimer

const finalPlan = computed(() => parseJson(selected.value?.finalPlanJson))
const proposals = computed(() => parseJson(selected.value?.proposalsJson) || {})
const critiques = computed(() => parseJson(selected.value?.critiquesJson) || {})
const roadmap = computed(() => finalPlan.value?.roadmap || [])
const tasks = computed(() => finalPlan.value?.tasks || [])
const risks = computed(() => finalPlan.value?.risks || [])
const metrics = computed(() => finalPlan.value?.acceptanceMetrics || [])
const heldCount = computed(() => (progress.value?.epics || []).reduce((sum, epic) => sum + epic.hold, 0))
const completedCount = computed(() => (progress.value?.epics || []).reduce((sum, epic) => sum + epic.completed, 0))

function parseJson(value) {
  if (!value) return null
  try {
    return JSON.parse(value)
  } catch {
    return null
  }
}

function prettyJson(value) {
  const parsed = parseJson(value)
  return parsed ? JSON.stringify(parsed, null, 2) : (value || '—')
}

function statusType(status) {
  switch (status) {
    case 'Approved':
      return 'success'
    case 'Rejected':
    case 'Failed':
      return 'danger'
    case 'Queued':
    case 'Generating':
      return 'warning'
    case 'PendingReview':
      return 'primary'
    default:
      return 'info'
  }
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    items.value = await listProjectPlans({ status: statusFilter.value || undefined, limit: 50 })
    if (selected.value) {
      const refreshed = items.value.find((item) => item.id === selected.value.id)
      if (refreshed && refreshed.status !== selected.value.status) {
        selected.value = await getProjectPlan(selected.value.id)
        await loadProgress()
      }
    }
  } catch (exception) {
    error.value = exception.message
  } finally {
    loading.value = false
  }
}

async function loadProjects() {
  try {
    projects.value = await listProjects()
    const preferred = projects.value.find((project) => project.slug === '3derp')
    form.projectId = preferred?.id || projects.value[0]?.id || ''
  } catch {
    projects.value = []
  }
}

function openCreate() {
  Object.assign(form, {
    projectId: projects.value.find((project) => project.slug === '3derp')?.id || projects.value[0]?.id || '',
    title: '',
    goal: '',
    constraints: '',
    repository: 'wu1234a/3dERP',
    baseBranch: 'main',
    remote: 'origin',
    tokenEnvironmentVariable: 'AGENTOS_GITHUB_TOKEN',
    requirePullRequest: true
  })
  createVisible.value = true
}

async function save() {
  if (!form.projectId || !form.title.trim() || !form.goal.trim()) {
    ElMessage.warning('项目、标题和目标不能为空')
    return
  }

  try {
    await createProjectPlan({
      projectId: form.projectId,
      title: form.title,
      goal: form.goal,
      constraints: form.constraints || null,
      delivery: {
        repository: form.repository,
        baseBranch: form.baseBranch,
        remote: form.remote,
        tokenEnvironmentVariable: form.tokenEnvironmentVariable,
        requirePullRequest: form.requirePullRequest
      }
    })
    ElMessage.success('立项已创建，规划委员会开始工作')
    createVisible.value = false
    await load()
  } catch (exception) {
    ElMessage.error(exception.message)
  }
}

async function loadProgress() {
  if (!selected.value) return
  try {
    progress.value = await getProjectPlanProgress(selected.value.id)
  } catch {
    progress.value = null
  }
}

async function openDetail(row) {
  detailLoading.value = true
  detailVisible.value = true
  activeTab.value = 'plan'
  feedback.value = ''
  try {
    selected.value = await getProjectPlan(row.id)
    await loadProgress()
  } catch (exception) {
    ElMessage.error(exception.message)
  } finally {
    detailLoading.value = false
  }
}

async function decide(action) {
  if (!selected.value) return
  const verb = action === 'approve' ? '批准' : '拒绝'
  try {
    await ElMessageBox.confirm(`确认${verb}「${selected.value.title}」？`, `${verb}立项`, {
      type: action === 'approve' ? 'success' : 'warning'
    })
    if (action === 'approve') {
      await approveProjectPlan(selected.value.id)
      ElMessage.success('已批准，任务已进入 Backlog')
    } else {
      await rejectProjectPlan(selected.value.id)
      ElMessage.success('已拒绝')
    }
    await load()
    selected.value = await getProjectPlan(selected.value.id)
  } catch (exception) {
    if (exception !== 'cancel' && exception !== 'close') {
      ElMessage.error(exception.message || String(exception))
    }
  }
}

async function release() {
  if (!selected.value) return
  try {
    await ElMessageBox.confirm(
      `将 ${heldCount.value} 条 Hold 任务恢复为 Pending，并开始执行。确认继续？`,
      '启动执行',
      { type: 'warning' }
    )
    releasing.value = true
    const result = await releaseProjectPlan(selected.value.id)
    ElMessage.success(`已启动 ${result.released} 条 Backlog`)
    await load()
    await loadProgress()
  } catch (exception) {
    if (exception !== 'cancel' && exception !== 'close') {
      ElMessage.error(exception.message || String(exception))
    }
  } finally {
    releasing.value = false
  }
}

async function revise() {
  if (!selected.value) return
  if (feedback.value.trim().length < 5) {
    ElMessage.warning('请填写至少 5 个字符的修订意见')
    return
  }

  try {
    await ElMessageBox.confirm('将丢弃当前草稿并重新运行规划委员会，确认继续？', '按意见重新规划', {
      type: 'warning'
    })
    revising.value = true
    await reviseProjectPlan(selected.value.id, feedback.value)
    ElMessage.success('已提交修订意见，规划委员会重新工作')
    feedback.value = ''
    await load()
    selected.value = await getProjectPlan(selected.value.id)
    await loadProgress()
  } catch (exception) {
    if (exception !== 'cancel' && exception !== 'close') {
      ElMessage.error(exception.message || String(exception))
    }
  } finally {
    revising.value = false
  }
}

onMounted(async () => {
  await Promise.all([load(), loadProjects()])
  pollTimer = setInterval(load, 10000)
})

onUnmounted(() => clearInterval(pollTimer))
</script>

<template>
  <div data-testid="project-plans-page">
    <div class="page-head">
      <div>
        <h2>Project Plans</h2>
        <p class="muted">多 Agent 立项：独立提案、交叉评审、Chair 综合、人工批准后进入 Backlog</p>
      </div>
      <div class="actions">
        <el-select v-model="statusFilter" placeholder="全部状态" clearable style="width: 160px" @change="load">
          <el-option label="全部状态" value="" />
          <el-option label="Queued" value="Queued" />
          <el-option label="Generating" value="Generating" />
          <el-option label="PendingReview" value="PendingReview" />
          <el-option label="Approved" value="Approved" />
          <el-option label="Rejected" value="Rejected" />
          <el-option label="Failed" value="Failed" />
        </el-select>
        <el-button @click="load">刷新</el-button>
        <el-button type="primary" @click="openCreate">新建立项</el-button>
      </div>
    </div>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />

    <el-table v-loading="loading" :data="items" empty-text="暂无立项" data-testid="project-plans-table">
      <el-table-column prop="title" label="立项" min-width="240">
        <template #default="{ row }">
          <div class="plan-title">{{ row.title }}</div>
          <div class="muted plan-goal">{{ row.goal }}</div>
        </template>
      </el-table-column>
      <el-table-column label="状态" width="130">
        <template #default="{ row }">
          <el-tag :type="statusType(row.status)">{{ row.status }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="modelProfileCode" label="规划模型" width="160" />
      <el-table-column label="创建时间" width="180">
        <template #default="{ row }">{{ formatUtc(row.createdAtUtc) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="100" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="openDetail(row)">查看</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="createVisible" title="新建立项" width="680px">
      <el-form label-width="120px">
        <el-form-item label="项目">
          <el-select v-model="form.projectId" style="width: 100%">
            <el-option v-for="project in projects" :key="project.id" :label="`${project.name} (${project.slug})`" :value="project.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="标题"><el-input v-model="form.title" maxlength="200" show-word-limit /></el-form-item>
        <el-form-item label="目标"><el-input v-model="form.goal" type="textarea" :rows="4" maxlength="8000" show-word-limit /></el-form-item>
        <el-form-item label="约束">
          <el-input v-model="form.constraints" type="textarea" :rows="3" maxlength="8000" show-word-limit placeholder="预算、周期、团队、技术栈、不可改动项、风险偏好" />
        </el-form-item>
        <el-divider content-position="left">交付配置</el-divider>
        <el-form-item label="仓库"><el-input v-model="form.repository" placeholder="owner/repo" /></el-form-item>
        <el-form-item label="基础分支"><el-input v-model="form.baseBranch" /></el-form-item>
        <el-form-item label="Token 环境变量"><el-input v-model="form.tokenEnvironmentVariable" /></el-form-item>
        <el-form-item label="自动 PR"><el-switch v-model="form.requirePullRequest" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false">取消</el-button>
        <el-button type="primary" @click="save">开始规划</el-button>
      </template>
    </el-dialog>

    <el-drawer v-model="detailVisible" title="项目立项详情" size="78%">
      <div v-loading="detailLoading">
        <template v-if="selected">
          <div class="detail-head">
            <div>
              <h3>{{ selected.title }}</h3>
              <div class="muted">{{ selected.goal }}</div>
            </div>
            <el-tag :type="statusType(selected.status)" size="large">{{ selected.status }}</el-tag>
          </div>

          <el-descriptions :column="3" border class="detail-meta">
            <el-descriptions-item label="规划模型">{{ selected.modelProfileCode }}</el-descriptions-item>
            <el-descriptions-item label="修订版本">v{{ selected.revision }}</el-descriptions-item>
            <el-descriptions-item label="创建时间">{{ formatUtc(selected.createdAtUtc) }}</el-descriptions-item>
            <el-descriptions-item label="决策时间">{{ formatUtc(selected.decidedAtUtc) }}</el-descriptions-item>
            <el-descriptions-item label="约束" :span="2">{{ selected.constraints || '—' }}</el-descriptions-item>
            <el-descriptions-item label="人工意见" :span="2">{{ selected.feedback || '—' }}</el-descriptions-item>
            <el-descriptions-item v-if="selected.error" label="错误" :span="3">
              <span class="danger-text">{{ selected.error }}</span>
            </el-descriptions-item>
          </el-descriptions>

          <el-alert
            v-if="selected.status === 'Queued' || selected.status === 'Generating'"
            title="规划委员会正在工作：3 份提案 → 3 份评审 → Chair 综合。通常需要 1-5 分钟。"
            type="info"
            show-icon
            :closable="false"
            class="plan-progress"
          />

          <el-card v-if="progress" shadow="never" class="progress-card">
            <template #header>
              <div class="progress-head">
                <b>执行进度</b>
                <span class="muted">共 {{ progress.total }} 条 · 已完成 {{ completedCount }} · Hold {{ heldCount }}</span>
              </div>
            </template>
            <el-table :data="progress.epics" size="small" empty-text="暂无 Backlog">
              <el-table-column prop="epicKey" label="Epic" width="110" />
              <el-table-column prop="total" label="总数" width="80" />
              <el-table-column prop="pending" label="Pending" width="90" />
              <el-table-column prop="hold" label="Hold" width="80" />
              <el-table-column prop="active" label="执行中" width="90" />
              <el-table-column prop="completed" label="完成" width="80" />
              <el-table-column prop="failed" label="失败" width="80" />
            </el-table>
          </el-card>

          <el-tabs v-model="activeTab" class="plan-tabs">
            <el-tab-pane label="最终方案" name="plan">
              <template v-if="finalPlan">
                <el-card shadow="never" class="plan-card">
                  <template #header><b>推荐方案</b></template>
                  <p class="recommended">{{ finalPlan.recommendedOption }}</p>
                  <p class="muted">{{ finalPlan.rationale }}</p>
                </el-card>

                <h4>路线图</h4>
                <el-timeline>
                  <el-timeline-item
                    v-for="milestone in roadmap"
                    :key="milestone.milestone"
                    :timestamp="milestone.milestone"
                    placement="top"
                  >
                    <b>{{ milestone.goal }}</b>
                    <div class="muted">退出条件：{{ milestone.exitCriteria }}</div>
                  </el-timeline-item>
                </el-timeline>

                <h4>任务拆解（{{ tasks.length }}）</h4>
                <el-table :data="tasks" size="small" empty-text="暂无任务">
                  <el-table-column prop="epic" label="Epic" width="90" />
                  <el-table-column prop="kind" label="类型" width="100" />
                  <el-table-column prop="title" label="任务" min-width="240" />
                  <el-table-column prop="instruction" label="指令" min-width="360" show-overflow-tooltip />
                </el-table>

                <div class="two-columns">
                  <div>
                    <h4>风险</h4>
                    <ul class="plain-list">
                      <li v-for="risk in risks" :key="risk.risk"><b>{{ risk.risk }}</b>：{{ risk.mitigation }}</li>
                    </ul>
                  </div>
                  <div>
                    <h4>验收指标</h4>
                    <ul class="plain-list">
                      <li v-for="metric in metrics" :key="metric">{{ metric }}</li>
                    </ul>
                  </div>
                </div>
              </template>
              <el-empty v-else description="最终方案尚未生成" />
            </el-tab-pane>

            <el-tab-pane label="提案" name="proposals">
              <el-collapse>
                <el-collapse-item v-for="(content, role) in proposals" :key="role" :title="role" :name="role">
                  <pre class="plan-pre">{{ content }}</pre>
                </el-collapse-item>
              </el-collapse>
            </el-tab-pane>

            <el-tab-pane label="交叉评审" name="critiques">
              <el-collapse>
                <el-collapse-item v-for="(content, role) in critiques" :key="role" :title="role" :name="role">
                  <pre class="plan-pre">{{ content }}</pre>
                </el-collapse-item>
              </el-collapse>
            </el-tab-pane>

            <el-tab-pane label="原始 JSON" name="raw">
              <pre class="plan-pre">{{ prettyJson(selected.finalPlanJson) }}</pre>
            </el-tab-pane>
          </el-tabs>

          <div v-if="selected.status === 'PendingReview' || selected.status === 'Failed'" class="revise-box">
            <div class="revise-label">人工修订意见</div>
            <el-input
              v-model="feedback"
              type="textarea"
              :rows="3"
              maxlength="8000"
              show-word-limit
              placeholder="例如：首期只做库存报表，不做采购/销售；必须复用现有权限过滤。"
            />
            <el-button type="warning" plain :loading="revising" class="revise-button" @click="revise">
              按意见重新规划
            </el-button>
          </div>

          <div class="drawer-actions">
            <el-button
              v-if="selected.status === 'PendingReview'"
              type="success"
              @click="decide('approve')"
            >
              批准并生成 Backlog
            </el-button>
            <el-button
              v-if="selected.status === 'PendingReview'"
              type="danger"
              plain
              @click="decide('reject')"
            >
              拒绝
            </el-button>
            <el-button
              v-if="selected.status === 'Approved' && heldCount > 0"
              type="primary"
              :loading="releasing"
              @click="release"
            >
              启动执行（{{ heldCount }}）
            </el-button>
          </div>
        </template>
      </div>
    </el-drawer>
  </div>
</template>

<style scoped>
.page-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 16px;
}

.page-head h2 {
  margin: 0 0 4px;
}

.actions {
  display: flex;
  gap: 8px;
  align-items: center;
}

.muted {
  color: #6b7280;
}

.plan-title {
  font-weight: 600;
}

.plan-goal {
  margin-top: 4px;
  font-size: 12px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 620px;
}

.detail-head {
  display: flex;
  justify-content: space-between;
  gap: 16px;
  align-items: flex-start;
  margin-bottom: 16px;
}

.detail-head h3 {
  margin: 0 0 6px;
}

.detail-meta {
  margin-bottom: 16px;
}

.plan-progress {
  margin-bottom: 12px;
}

.progress-card {
  margin-bottom: 12px;
}

.progress-head {
  display: flex;
  justify-content: space-between;
  gap: 12px;
}

.revise-box {
  margin-top: 18px;
  padding: 14px;
  border: 1px solid #fde68a;
  background: #fffbeb;
  border-radius: 8px;
}

.revise-label {
  font-weight: 600;
  margin-bottom: 8px;
}

.revise-button {
  margin-top: 8px;
}

.plan-tabs {
  margin-top: 8px;
}

.plan-card {
  margin-bottom: 18px;
}

.recommended {
  font-size: 15px;
  line-height: 1.7;
  margin-top: 0;
}

.two-columns {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 24px;
}

.plain-list {
  padding-left: 18px;
  line-height: 1.8;
}

.plan-pre {
  white-space: pre-wrap;
  word-break: break-word;
  background: #f8fafc;
  border: 1px solid #e5e7eb;
  border-radius: 6px;
  padding: 12px;
  font-size: 12px;
  max-height: 520px;
  overflow: auto;
}

.danger-text {
  color: #dc2626;
}

.drawer-actions {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
  border-top: 1px solid #e5e7eb;
  padding-top: 16px;
  margin-top: 16px;
}
</style>
