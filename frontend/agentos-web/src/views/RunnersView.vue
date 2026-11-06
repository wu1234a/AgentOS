<script setup>
import { onMounted, ref } from 'vue'
import { Refresh } from '@element-plus/icons-vue'
import { listWorkers } from '../api/observability'
import { formatUtc, statusTagType } from '../utils/format'

const items = ref([])
const loading = ref(false)
const error = ref('')

async function load() {
  loading.value = true
  error.value = ''
  try {
    const data = await listWorkers()
    items.value = data.items
  } catch (exception) {
    error.value = exception.message
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div>
    <div class="page-head">
      <div>
        <h2>Runners</h2>
        <p class="muted">Worker 在线状态、心跳与版本</p>
      </div>
      <el-button :loading="loading" @click="load">
        <el-icon><Refresh /></el-icon>
        刷新
      </el-button>
    </div>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />

    <el-table v-loading="loading" :data="items" empty-text="暂无 Runner">
      <el-table-column prop="workerCode" label="Worker" min-width="180" />
      <el-table-column prop="displayName" label="名称" min-width="160" />
      <el-table-column prop="status" label="状态" width="110">
        <template #default="{ row }">
          <el-tag :type="statusTagType(row.status)" size="small">{{ row.status }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="最后心跳" width="180">
        <template #default="{ row }">{{ formatUtc(row.lastHeartbeatAtUtc) }}</template>
      </el-table-column>
      <el-table-column prop="platform" label="平台" width="110" />
      <el-table-column prop="architecture" label="架构" width="90" />
      <el-table-column prop="version" label="版本" width="100" />
      <el-table-column prop="hostName" label="主机" min-width="160" />
    </el-table>
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
</style>
