<script setup>
import { onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { getWorkerStats, listWorkers, setWorkerEnabled } from '../api/workers'
import { formatUtc } from '../utils/format'

const items = ref([])
const stats = ref({ total: 0, online: 0, offline: 0, disabled: 0, runningTasks: 0 })
const loading = ref(false)
const error = ref('')

async function load() {
  loading.value = true
  error.value = ''
  try {
    const [workers, summary] = await Promise.all([listWorkers(), getWorkerStats()])
    items.value = workers
    stats.value = summary
  } catch (exception) {
    error.value = exception.message
  } finally {
    loading.value = false
  }
}

async function toggle(row) {
  try {
    await setWorkerEnabled(row.id, row.status !== 'Online')
    ElMessage.success('Worker 状态已更新')
    await load()
  } catch (exception) {
    ElMessage.error(exception.message)
  }
}

onMounted(load)
</script>

<template>
  <div>
    <div class="page-head">
      <div>
        <h2>Workers</h2>
        <p class="muted">Runner 心跳、运行任务与启停；禁用只停止新领取</p>
      </div>
      <el-button @click="load">刷新</el-button>
    </div>
    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />
    <el-row :gutter="12" class="metric-row">
      <el-col :span="4"><el-card><div class="metric">{{ stats.total }}</div><div class="muted">总数</div></el-card></el-col>
      <el-col :span="5"><el-card><div class="metric">{{ stats.online }}</div><div class="muted">Online</div></el-card></el-col>
      <el-col :span="5"><el-card><div class="metric">{{ stats.offline }}</div><div class="muted">Offline</div></el-card></el-col>
      <el-col :span="5"><el-card><div class="metric">{{ stats.disabled }}</div><div class="muted">Disabled</div></el-card></el-col>
      <el-col :span="5"><el-card><div class="metric">{{ stats.runningTasks }}</div><div class="muted">运行任务</div></el-card></el-col>
    </el-row>
    <el-table v-loading="loading" :data="items" empty-text="暂无 Worker">
      <el-table-column prop="workerCode" label="Worker" min-width="160" />
      <el-table-column prop="hostName" label="主机" min-width="160" />
      <el-table-column prop="platform" label="平台" width="120" />
      <el-table-column prop="version" label="版本" width="120" />
      <el-table-column label="状态" width="110">
        <template #default="{ row }">
          <el-tag :type="row.status === 'Online' ? 'success' : row.status === 'Disabled' ? 'info' : 'warning'">{{ row.status }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="runningTaskCount" label="运行任务" width="100" />
      <el-table-column label="最后心跳" width="180">
        <template #default="{ row }">{{ formatUtc(row.lastHeartbeatAtUtc) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="120">
        <template #default="{ row }">
          <el-button link :type="row.status === 'Disabled' ? 'success' : 'warning'" @click="toggle(row)">
            {{ row.status === 'Disabled' ? '启用' : '禁用' }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>
  </div>
</template>

<style scoped>
.metric-row { margin-bottom: 16px; }
.metric { font-size: 24px; font-weight: 600; }
</style>
