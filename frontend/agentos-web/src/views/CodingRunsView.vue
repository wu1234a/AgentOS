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
const form = reactive({ draftId: '', agentId: '', priority: 'normal', timeoutSeconds: 1800 })

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
  Object.assign(form, { draftId: '', agentId: '', priority: 'normal', timeoutSeconds: 1800 })
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
      timeoutSeconds: form.timeoutSeconds
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
  } catch (exception) {
    ElMessage.error(exception.message)
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
  <div>
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
.pager {
  margin-top: 16px;
  justify-content: flex-end;
}
</style>
