<script setup>
import { computed, onMounted } from 'vue'
import { Refresh } from '@element-plus/icons-vue'
import { useHealthStore } from '../stores/health'

const health = useHealthStore()

onMounted(() => health.refresh())

const liveStatus = computed(() => health.live?.status ?? 'Unknown')
const readyStatus = computed(() => health.ready?.status ?? 'Unknown')

function tagType(status) {
  if (status === 'Healthy') return 'success'
  if (status === 'Degraded') return 'warning'
  return 'danger'
}
</script>

<template>
  <div class="dashboard">
    <div class="page-head">
      <div>
        <h2>Dashboard</h2>
        <p class="muted">AgentOS 服务状态与数据库就绪情况</p>
      </div>
      <el-button :loading="health.loading" @click="health.refresh">
        <el-icon><Refresh /></el-icon>
        刷新
      </el-button>
    </div>

    <el-alert
      v-if="health.error"
      :title="health.error"
      type="error"
      show-icon
      :closable="false"
      class="error-alert"
    />

    <el-row :gutter="16">
      <el-col :xs="24" :md="12">
        <el-card class="status-card" shadow="never">
          <template #header>
            <div class="card-head">
              <span>API Live</span>
              <el-tag :type="tagType(liveStatus)">{{ liveStatus }}</el-tag>
            </div>
          </template>
          <p class="muted">进程存活检查，不依赖数据库。</p>
          <p class="endpoint">GET /health/live</p>
        </el-card>
      </el-col>
      <el-col :xs="24" :md="12">
        <el-card class="status-card" shadow="never">
          <template #header>
            <div class="card-head">
              <span>API Ready</span>
              <el-tag :type="tagType(readyStatus)">{{ readyStatus }}</el-tag>
            </div>
          </template>
          <p class="muted">就绪检查，包含 SQL Server 连通性。</p>
          <p class="endpoint">GET /health/ready</p>
          <ul v-if="health.ready?.checks?.length" class="checks">
            <li v-for="check in health.ready.checks" :key="check.name">
              {{ check.name }}: {{ check.status }}
            </li>
          </ul>
        </el-card>
      </el-col>
    </el-row>

    <p v-if="health.lastCheckedAt" class="muted last-checked">
      最后检查：{{ health.lastCheckedAt.toLocaleString() }}
    </p>
  </div>
</template>

<style scoped>
.page-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 20px;
}

.page-head h2 {
  margin: 0 0 4px;
}

.muted {
  color: #6b7280;
  margin: 0;
}

.error-alert {
  margin-bottom: 16px;
}

.status-card {
  margin-bottom: 16px;
}

.card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-weight: 600;
}

.endpoint {
  margin: 8px 0 0;
  font-family: Consolas, monospace;
  font-size: 12px;
  color: #6b7280;
}

.checks {
  margin: 12px 0 0;
  padding-left: 18px;
  font-size: 13px;
  color: #374151;
}

.last-checked {
  margin-top: 8px;
  font-size: 13px;
}
</style>
