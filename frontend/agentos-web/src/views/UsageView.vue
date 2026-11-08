<script setup>
import { onMounted, ref } from 'vue'
import { getUsageStatistics } from '../api/usage'

const items = ref([])
const range = ref([])
const loading = ref(false)
const error = ref('')
const totals = ref({ calls: 0, tokens: 0, cost: 0, missing: 0 })

async function load() {
  loading.value = true
  error.value = ''
  try {
    const [from, to] = range.value || []
    const data = await getUsageStatistics({ from, to })
    items.value = data.items
    totals.value = data.items.reduce((accumulator, item) => ({
      calls: accumulator.calls + item.callCount,
      tokens: accumulator.tokens + item.totalTokens,
      cost: accumulator.cost + (item.estimatedCost || 0),
      missing: accumulator.missing + (item.pricingStatus === 'MissingPrice' ? 1 : 0)
    }), { calls: 0, tokens: 0, cost: 0, missing: 0 })
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
        <h2>Usage</h2>
        <p class="muted">按项目/日期/模型聚合 Token；费用为配置单价估算</p>
      </div>
      <el-date-picker v-model="range" type="datetimerange" start-placeholder="From" end-placeholder="To" @change="load" />
    </div>
    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />
    <el-row :gutter="12" class="metric-row">
      <el-col :span="6"><el-card><div class="metric">{{ totals.calls }}</div><div class="muted">调用次数</div></el-card></el-col>
      <el-col :span="6"><el-card><div class="metric">{{ totals.tokens }}</div><div class="muted">总 Token</div></el-card></el-col>
      <el-col :span="6"><el-card><div class="metric">${{ totals.cost.toFixed(4) }}</div><div class="muted">估算费用</div></el-card></el-col>
      <el-col :span="6"><el-card><div class="metric">{{ totals.missing }}</div><div class="muted">缺失价格分组</div></el-card></el-col>
    </el-row>
    <el-table v-loading="loading" :data="items" empty-text="暂无用量">
      <el-table-column prop="date" label="日期" width="120" />
      <el-table-column prop="projectId" label="项目" min-width="220" />
      <el-table-column prop="provider" label="Provider" width="120" />
      <el-table-column prop="model" label="模型" min-width="160" />
      <el-table-column prop="callCount" label="调用" width="80" />
      <el-table-column prop="inputTokens" label="输入 Token" width="130" />
      <el-table-column prop="outputTokens" label="输出 Token" width="130" />
      <el-table-column prop="totalTokens" label="总 Token" width="130" />
      <el-table-column label="估算费用" width="130">
        <template #default="{ row }">{{ row.estimatedCost == null ? 'N/A' : `$${Number(row.estimatedCost).toFixed(4)}` }}</template>
      </el-table-column>
      <el-table-column prop="pricingStatus" label="定价" width="120" />
    </el-table>
  </div>
</template>

<style scoped>
.metric-row { margin-bottom: 16px; }
.metric { font-size: 24px; font-weight: 600; }
</style>
