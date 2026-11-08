<script setup>
import { onMounted, ref } from 'vue'
import { Refresh } from '@element-plus/icons-vue'
import { listEvents } from '../api/observability'
import { formatUtc, statusTagType } from '../utils/format'

const items = ref([])
const total = ref(0)
const page = ref(1)
const pageSize = 20
const loading = ref(false)
const error = ref('')

async function load() {
  loading.value = true
  error.value = ''
  try {
    const data = await listEvents({ page: page.value, pageSize })
    items.value = data.items
    total.value = data.total
  } catch (exception) {
    error.value = exception.message
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div data-testid="events-page">
    <div class="page-head">
      <div>
        <h2>Events</h2>
        <p class="muted">事件账本与关联任务</p>
      </div>
      <el-button :loading="loading" @click="load">
        <el-icon><Refresh /></el-icon>
        刷新
      </el-button>
    </div>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />

    <el-table v-loading="loading" :data="items" empty-text="暂无事件">
      <el-table-column prop="source" label="来源" width="130" />
      <el-table-column prop="eventType" label="类型" width="180" />
      <el-table-column prop="status" label="状态" width="110">
        <template #default="{ row }">
          <el-tag :type="statusTagType(row.status)" size="small">{{ row.status }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="summary" label="摘要" min-width="240" show-overflow-tooltip />
      <el-table-column label="接收时间" width="180">
        <template #default="{ row }">{{ formatUtc(row.receivedAtUtc) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="100">
        <template #default="{ row }">
          <router-link :to="`/events/${row.id}`">详情</router-link>
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
.pager {
  margin-top: 16px;
  justify-content: flex-end;
}
</style>
