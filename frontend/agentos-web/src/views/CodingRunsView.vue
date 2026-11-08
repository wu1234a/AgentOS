<script setup>
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import {
  cancelCodingRun,
  createCodingRun,
  getCodingRun,
  listCodingRuns,
  retryCodingRun
} from '../api/codingRuns'
import { listAgents } from '../api/agents'
import { listDrafts } from '../api/planner'
import { createReview, getReview } from '../api/reviews'
import { createQa, getLatestQa } from '../api/qa'
import { formatUtc } from '../utils/format'

const items = ref([])
const total = ref(0)
const page = ref(1)
const pageSize = 50
const loading = ref(false)
const error = ref('')
const statusFilter = ref('')
const agents = ref([])
const drafts = ref([])
const createVisible = ref(false)
const detailVisible = ref(false)
const detail = ref(null)
const review = ref(null)
const reviewLoading = ref(false)
const reviewError = ref('')
const qa = ref(null)
const qaLoading = ref(false)
const qaError = ref('')
const form = reactive({
  draftId: '',
  agentId: '',
  priority: 'normal',
  timeoutSeconds: 1800,
  validationCommands: '',
  baseRevision: ''
})

async function load() {
  loading.value = true
  error.value = ''
  try {
    const data = await listCodingRuns({
      status: statusFilter.value || undefined,
      limit: pageSize,
      offset: (page.value - 1) * pageSize
    })
    items.value = data.items
    total.value = data.total
  } catch (exception) {
    error.value = exception.message
  } finally {
    loading.value = false
  }
}

async function loadOptions() {
  try {
    const [agentData, draftData] = await Promise.all([
      listAgents({ limit: 200 }),
      listDrafts({ status: 'Approved', limit: 200 })
    ])
    agents.value = agentData.items.filter((agent) => !agent.isArchived)
    drafts.value = draftData.items
  } catch {
    agents.value = []
    drafts.value = []
  }
}

function openCreate() {
  Object.assign(form, {
    draftId: '',
    agentId: '',
    priority: 'normal',
    timeoutSeconds: 1800,
    validationCommands: '',
    baseRevision: ''
  })
  createVisible.value = true
}

async function save() {
  const agent = agents.value.find((candidate) => candidate.id === form.agentId)
  if (!agent?.projectId) {
    ElMessage.error('请选择已绑定项目的 Agent')
    return
  }

  try {
    await createCodingRun({
      draftId: form.draftId,
      agentId: form.agentId,
      projectId: agent.projectId,
      priority: form.priority,
      timeoutSeconds: form.timeoutSeconds,
      validationCommands: form.validationCommands
        .split('\n')
        .map((line) => line.trim())
        .filter(Boolean),
      baseRevision: form.baseRevision.trim() || null
    })
    ElMessage.success('Coding Run 已创建')
    createVisible.value = false
    await load()
  } catch (exception) {
    ElMessage.error(exception.message)
  }
}

async function openDetail(row) {
  try {
    detail.value = await getCodingRun(row.id)
    detailVisible.value = true
    review.value = null
    reviewError.value = ''
    qa.value = null
    qaError.value = ''
    try {
      review.value = await getReview(row.id)
    } catch {
      review.value = null
    }
    try {
      qa.value = await getLatestQa(row.id)
    } catch {
      qa.value = null
    }
  } catch (exception) {
    ElMessage.error(exception.message)
  }
}

async function startQa(row) {
  qaLoading.value = true
  qaError.value = ''
  try {
    qa.value = await createQa(row.id)
  } catch (exception) {
    qaError.value = exception.message
  } finally {
    qaLoading.value = false
  }
}

async function startReview(row) {
  reviewLoading.value = true
  reviewError.value = ''
  try {
    review.value = await createReview(row.id)
  } catch (exception) {
    reviewError.value = exception.message
  } finally {
    reviewLoading.value = false
  }
}

async function cancel(row) {
  try {
    await cancelCodingRun(row.id)
    ElMessage.success('已请求取消')
    await load()
    if (detail.value) detail.value = await getCodingRun(row.id)
  } catch (exception) {
    ElMessage.error(exception.message)
  }
}

async function retry(row) {
  try {
    await retryCodingRun(row.id)
    ElMessage.success('已重新入队')
    await load()
    if (detail.value) detail.value = await getCodingRun(row.id)
  } catch (exception) {
    ElMessage.error(exception.message)
  }
}

function tagType(status) {
  if (status === 'Succeeded') return 'success'
  if (status === 'Failed') return 'danger'
  if (status === 'Canceled' || status === 'Canceling') return 'info'
  if (status === 'Running') return 'warning'
  return ''
}

onMounted(async () => {
  await load()
  await loadOptions()
})
</script>

<template>
  <div data-testid="coding-runs-page">
    <div class="page-head">
      <div>
        <h2>Coding Runs</h2>
        <p class="muted">已批准草案 → 不可变快照 → Runner 任务；支持取消与重试</p>
      </div>
      <div class="actions">
        <el-select
          v-model="statusFilter"
          placeholder="全部状态"
          style="width: 160px"
          @change="() => { page = 1; load() }"
        >
          <el-option label="全部状态" value="" />
          <el-option v-for="status in ['Queued', 'Running', 'Succeeded', 'Failed', 'Canceled']" :key="status" :label="status" :value="status" />
        </el-select>
        <el-button type="primary" @click="openCreate">创建 Run</el-button>
      </div>
    </div>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />

    <el-table v-loading="loading" :data="items" empty-text="暂无 Coding Run">
      <el-table-column prop="title" label="草案" min-width="240" />
      <el-table-column label="状态" width="130">
        <template #default="{ row }">
          <el-tag :type="tagType(row.status)" size="small">{{ row.status }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="taskStatus" label="任务状态" width="120" />
      <el-table-column prop="attemptCount" label="Attempt" width="90" />
      <el-table-column label="创建时间" width="180">
        <template #default="{ row }">{{ formatUtc(row.createdAtUtc) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="220">
        <template #default="{ row }">
          <el-button link type="primary" @click="openDetail(row)">详情</el-button>
          <el-button
            v-if="!['Succeeded', 'Failed', 'Canceled'].includes(row.status)"
            link
            type="warning"
            @click="cancel(row)"
          >
            取消
          </el-button>
          <el-button
            v-if="['Failed', 'Canceled'].includes(row.status)"
            link
            type="success"
            @click="retry(row)"
          >
            重试
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-pagination
      class="pager"
      layout="prev, pager, next, total"
      :total="total"
      :page-size="pageSize"
      :current-page="page"
      @current-change="(value) => { page = value; load() }"
    />

    <el-dialog v-model="createVisible" title="创建 Coding Run" width="620px">
      <el-form label-width="120px">
        <el-form-item label="已批准草案">
          <el-select v-model="form.draftId" filterable placeholder="选择草案">
            <el-option v-for="draft in drafts" :key="draft.id" :label="draft.title" :value="draft.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="Agent">
          <el-select v-model="form.agentId" filterable placeholder="选择编码 Agent">
            <el-option
              v-for="agent in agents"
              :key="agent.id"
              :label="`${agent.code} (${agent.role})`"
              :value="agent.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="优先级">
          <el-select v-model="form.priority">
            <el-option label="low" value="low" />
            <el-option label="normal" value="normal" />
            <el-option label="high" value="high" />
          </el-select>
        </el-form-item>
        <el-form-item label="超时(秒)">
          <el-input-number v-model="form.timeoutSeconds" :min="60" :max="7200" />
        </el-form-item>
        <el-form-item label="Base revision">
          <el-input v-model="form.baseRevision" placeholder="可选，commit SHA；用于生成 Review 变更集" />
        </el-form-item>
        <el-form-item label="校验命令">
          <el-input
            v-model="form.validationCommands"
            type="textarea"
            :rows="3"
            placeholder="每行一条，例如：dotnet test backend\AgentOS.slnx"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false">取消</el-button>
        <el-button type="primary" @click="save">创建</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="detailVisible" title="Coding Run 详情" width="760px">
      <template v-if="detail">
        <el-descriptions :column="2" border>
          <el-descriptions-item label="Run">{{ detail.run.id }}</el-descriptions-item>
          <el-descriptions-item label="状态">{{ detail.run.status }}</el-descriptions-item>
          <el-descriptions-item label="Task">{{ detail.run.taskId }}</el-descriptions-item>
          <el-descriptions-item label="尝试次数">{{ detail.run.attemptCount }}</el-descriptions-item>
          <el-descriptions-item label="错误码">{{ detail.run.errorCode || '-' }}</el-descriptions-item>
          <el-descriptions-item label="快照哈希">{{ detail.run.draftContentHash.slice(0, 16) }}…</el-descriptions-item>
        </el-descriptions>

        <h4>Review</h4>
        <div v-if="!review" class="review-start">
          <el-button type="primary" :loading="reviewLoading" @click="startReview(detail.run)">
            开始审查
          </el-button>
          <span class="muted">只读审查，不会修改代码或合并</span>
        </div>
        <el-alert v-if="reviewError" :title="reviewError" type="error" show-icon :closable="false" />
        <template v-if="review">
          <el-descriptions :column="2" border>
            <el-descriptions-item label="状态">{{ review.status }}</el-descriptions-item>
            <el-descriptions-item label="结论">{{ review.conclusion || '-' }}</el-descriptions-item>
            <el-descriptions-item label="尝试次数">{{ review.attemptCount }}</el-descriptions-item>
            <el-descriptions-item label="错误码">{{ review.errorCode || '-' }}</el-descriptions-item>
          </el-descriptions>
          <p v-if="review.summary" class="review-summary">{{ review.summary }}</p>
          <el-table v-if="review.findings?.length" :data="review.findings" size="small">
            <el-table-column prop="severity" label="级别" width="90" />
            <el-table-column prop="filePath" label="文件" min-width="180" />
            <el-table-column label="行" width="90">
              <template #default="{ row }">{{ row.startLine || '文件级' }}</template>
            </el-table-column>
            <el-table-column prop="reason" label="理由" min-width="200" />
            <el-table-column prop="suggestion" label="建议" min-width="200" />
          </el-table>
          <el-button
            v-if="review.status === 'Failed'"
            type="primary"
            :loading="reviewLoading"
            @click="startReview(detail.run)"
          >
            重试审查
          </el-button>
        </template>

        <h4>QA</h4>
        <div v-if="!qa" class="review-start">
          <el-button type="primary" :loading="qaLoading" @click="startQa(detail.run)">运行 QA</el-button>
          <span class="muted">读取已有校验结果，不重跑测试</span>
        </div>
        <el-alert v-if="qaError" :title="qaError" type="error" show-icon :closable="false" />
        <template v-if="qa">
          <el-descriptions :column="3" border>
            <el-descriptions-item label="状态">{{ qa.status }}</el-descriptions-item>
            <el-descriptions-item label="结论">{{ qa.conclusion }}</el-descriptions-item>
            <el-descriptions-item label="Attempt">{{ qa.attemptNo }}</el-descriptions-item>
            <el-descriptions-item label="命令">
              {{ qa.passedCount }} / {{ qa.commandCount }} 通过
            </el-descriptions-item>
            <el-descriptions-item label="用例">
              {{ qa.testPassed }} / {{ qa.testTotal }} 通过，失败 {{ qa.testFailed }}，错误 {{ qa.testError }}
            </el-descriptions-item>
            <el-descriptions-item label="覆盖率">
              行 {{ qa.lineCoverage ?? '—' }}{{ qa.lineCoverage == null ? '' : '%' }} /
              分支 {{ qa.branchCoverage ?? '—' }}{{ qa.branchCoverage == null ? '' : '%' }}
            </el-descriptions-item>
          </el-descriptions>
          <el-button type="primary" :loading="qaLoading" @click="startQa(detail.run)">重新运行 QA</el-button>
          <el-table :data="qa.commands" size="small">
            <el-table-column prop="commandText" label="命令" min-width="220" />
            <el-table-column prop="status" label="状态" width="100" />
            <el-table-column prop="exitCode" label="退出码" width="90" />
            <el-table-column prop="durationMs" label="耗时(ms)" width="100" />
            <el-table-column prop="outputExcerpt" label="输出摘要" min-width="240" />
          </el-table>
        </template>

        <h4>状态事件</h4>
        <el-timeline>
          <el-timeline-item
            v-for="event in detail.events"
            :key="event.sequence"
            :timestamp="formatUtc(event.createdAtUtc)"
          >
            #{{ event.sequence }} {{ event.eventType }} → {{ event.toStatus }}
          </el-timeline-item>
        </el-timeline>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.page-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}
.actions {
  display: flex;
  gap: 12px;
}
.muted {
  color: #6b7280;
  margin: 4px 0 0;
}
.review-start {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 12px;
}

.review-summary {
  margin: 12px 0;
  white-space: pre-wrap;
}

.pager {
  margin-top: 16px;
  justify-content: flex-end;
}
</style>
