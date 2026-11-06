<script setup>
import { onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Refresh } from '@element-plus/icons-vue'
import { listTasks } from '../api/observability'
import { formatUtc, statusTagType } from '../utils/format'

const route = useRoute()
const router = useRouter()
const items = ref([])
const total = ref(0)
const page = ref(1)
const pageSize = 20
const status = ref(route.query.status || '')
const loading = ref(false)
const error = ref('')

const statuses = ['', 'Queued', 'Running', 'RetryWait', 'Succeeded', 'Failed', 'Cancelled']

async function load() {
  loading.value = true
  error.value = ''
  try {
    const data = await listTasks({
      status: status.value || undefined,
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

function openTask(row) {
  router.push(`/tasks/${row.id}`)
}

watch(status, () => {
  page.value = 1
  load()
})

onMounted(load)
</script>

<template>
  <div>
    <div class="page-head">
      <div>
        <h2>Tasks</h2>
        <p class="muted">任务队列与执行状态</p>
      </div>
      <div class="actions">
        <el-select v-model="status" placeholder="全部状态" style="width: 160px">
          <el-option v-for="item in statuses" :key="item" :label="item || '全部状态'" :value="item" />
        </el-select>
        <el-button :loading="loading" @click="load">
          <el-icon><Refresh /></el-icon>
          刷新
        </el-button>
      </div>
    </div>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />

    <el-table v-loading="loading" :data="items" empty-text="暂无任务" @row-click="openTask">
      <el-table-column prop="taskCode" label="任务" min-width="200" />
      <el-table-column prop="title" label="标题" min-width="240" show-overflow-tooltip />
      <el-table-column prop="status" label="状态" width="110">
        <template #default="{ row }">
          <el-tag :type="statusTagType(row.status)" size="small">{{ row.status }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="priority" label="优先级" width="100" />
      <el-table-column prop="projectSlug" label="项目" width="140" />
      <el-table-column prop="agentCode" label="Agent" width="140" />
      <el-table-column prop="skillCode" label="Skill" width="140" />
      <el-table-column label="尝试" width="90">
        <template #default="{ row }">{{ row.attemptCount }}/{{ row.maxAttempts }}</template>
      </el-table-column>
      <el-table-column label="创建时间" width="180">
        <template #default="{ row }">{{ formatUtc(row.createdAtUtc) }}</template>
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
  gap: 8px;
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
