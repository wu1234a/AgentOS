<script setup>
import { onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { getEvent, getEventTasks } from '../api/observability'
import { formatUtc, statusTagType } from '../utils/format'

const route = useRoute()
const detail = ref(null)
const tasks = ref([])
const loading = ref(false)
const error = ref('')

async function load() {
  loading.value = true
  error.value = ''
  try {
    detail.value = await getEvent(route.params.id)
    const taskData = await getEventTasks(route.params.id)
    tasks.value = taskData.items
  } catch (exception) {
    error.value = exception.message
  } finally {
    loading.value = false
  }
}

onMounted(load)

function pretty(value) {
  return JSON.stringify(value ?? {}, null, 2)
}
</script>

<template>
  <div v-loading="loading">
    <div class="page-head">
      <div>
        <h2>Event Detail</h2>
        <p class="muted">
          <router-link to="/events">返回事件列表</router-link>
        </p>
      </div>
    </div>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />

    <template v-if="detail">
      <el-descriptions :column="3" border>
        <el-descriptions-item label="来源">{{ detail.source }}</el-descriptions-item>
        <el-descriptions-item label="类型">{{ detail.eventType }}</el-descriptions-item>
        <el-descriptions-item label="状态">
          <el-tag :type="statusTagType(detail.status)" size="small">{{ detail.status }}</el-tag>
        </el-descriptions-item>
        <el-descriptions-item label="项目" :span="2">{{ detail.projectSlug || '—' }}</el-descriptions-item>
        <el-descriptions-item label="接收时间">{{ formatUtc(detail.receivedAtUtc) }}</el-descriptions-item>
      </el-descriptions>

      <el-card shadow="never" class="section">
        <template #header>Payload</template>
        <pre class="payload">{{ pretty(detail.payload) }}</pre>
      </el-card>

      <el-card shadow="never" class="section">
        <template #header>关联任务 ({{ tasks.length }})</template>
        <el-table :data="tasks" empty-text="该事件没有关联任务">
          <el-table-column prop="taskCode" label="任务" min-width="200">
            <template #default="{ row }">
              <router-link :to="`/tasks/${row.id}`">{{ row.taskCode }}</router-link>
            </template>
          </el-table-column>
          <el-table-column prop="title" label="标题" min-width="240" show-overflow-tooltip />
          <el-table-column prop="status" label="状态" width="110">
            <template #default="{ row }">
              <el-tag :type="statusTagType(row.status)" size="small">{{ row.status }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="agentCode" label="Agent" width="140" />
        </el-table>
      </el-card>
    </template>
  </div>
</template>

<style scoped>
.page-head {
  margin-bottom: 16px;
}
.muted {
  color: #6b7280;
  margin: 4px 0 0;
}
.section {
  margin-top: 16px;
}
.payload {
  background: #111827;
  color: #e5e7eb;
  padding: 12px;
  border-radius: 6px;
  overflow: auto;
  max-height: 360px;
}
</style>
