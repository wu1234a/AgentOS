<script setup>
import { onMounted, ref } from 'vue'
import { Refresh } from '@element-plus/icons-vue'
import { getDashboard } from '../api/observability'
import { formatSeconds, formatUtc, statusTagType } from '../utils/format'
import { useTaskHub } from '../composables/useTaskHub'

const stats = ref(null)
const loading = ref(false)
const error = ref('')

const { connected, start, stop } = useTaskHub(() => load())

async function load() {
  loading.value = true
  error.value = ''
  try {
    stats.value = await getDashboard()
  } catch (exception) {
    error.value = exception.message
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  await load()
  await start()
})

import { onBeforeUnmount } from 'vue'
onBeforeUnmount(() => stop())

const cards = [
  ['事件总数', 'totalEvents'],
  ['重复事件', 'duplicateEvents'],
  ['任务总数', 'totalTasks'],
  ['排队中', 'queuedTasks'],
  ['运行中', 'runningTasks'],
  ['已成功', 'succeededTasks'],
  ['失败', 'failedTasks'],
  ['在线 Runner', 'onlineRunners'],
  ['租约超时', 'leaseExpiryCount']
]
</script>

<template>
  <div class="dashboard" data-testid="dashboard-page">
    <div class="page-head">
      <div>
        <h2>Dashboard</h2>
        <p class="muted">
          事件、任务与 Runner 概览
          <el-tag v-if="connected" type="success" size="small" style="margin-left: 8px">实时</el-tag>
        </p>
      </div>
      <el-button :loading="loading" @click="load">
        <el-icon><Refresh /></el-icon>
        刷新
      </el-button>
    </div>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" class="error-alert" />

    <el-row v-if="stats" :gutter="16" class="cards">
      <el-col v-for="[label, key] in cards" :key="key" :xs="12" :sm="8" :md="6" :lg="4">
        <el-card shadow="never" class="stat-card">
          <div class="stat-label">{{ label }}</div>
          <div class="stat-value">{{ stats[key] }}</div>
        </el-card>
      </el-col>
      <el-col :xs="12" :sm="8" :md="6" :lg="4">
        <el-card shadow="never" class="stat-card">
          <div class="stat-label">成功率</div>
          <div class="stat-value">{{ stats.successRate }}%</div>
        </el-card>
      </el-col>
      <el-col :xs="12" :sm="8" :md="6" :lg="4">
        <el-card shadow="never" class="stat-card">
          <div class="stat-label">平均等待 / 执行</div>
          <div class="stat-value small">
            {{ formatSeconds(stats.averageWaitSeconds) }} / {{ formatSeconds(stats.averageExecutionSeconds) }}
          </div>
        </el-card>
      </el-col>
    </el-row>

    <el-card v-if="stats" shadow="never" class="recent-failures">
      <template #header>
        <div class="card-head">
          <span>最近失败任务</span>
          <router-link to="/tasks?status=Failed">查看全部</router-link>
        </div>
      </template>
      <el-table :data="stats.recentFailures" empty-text="暂无失败任务">
        <el-table-column prop="taskCode" label="任务" min-width="180">
          <template #default="{ row }">
            <router-link :to="`/tasks/${row.id}`">{{ row.taskCode }}</router-link>
          </template>
        </el-table-column>
        <el-table-column prop="title" label="标题" min-width="220" show-overflow-tooltip />
        <el-table-column prop="projectSlug" label="项目" width="140" />
        <el-table-column prop="status" label="状态" width="110">
          <template #default="{ row }">
            <el-tag :type="statusTagType(row.status)" size="small">{{ row.status }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="完成时间" width="180">
          <template #default="{ row }">{{ formatUtc(row.completedAtUtc) }}</template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<style scoped>
.page-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}
.muted {
  color: #6b7280;
  margin: 4px 0 0;
}
.error-alert {
  margin-bottom: 16px;
}
.cards {
  margin-bottom: 16px;
}
.stat-card {
  margin-bottom: 16px;
}
.stat-label {
  color: #6b7280;
  font-size: 13px;
}
.stat-value {
  font-size: 24px;
  font-weight: 700;
  margin-top: 6px;
}
.stat-value.small {
  font-size: 16px;
}
.card-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
</style>
