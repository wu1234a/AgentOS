<script setup>
import { onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { getPipeline, listPipelines } from '../api/pipelines'
import { formatUtc } from '../utils/format'

const items = ref([])
const loading = ref(false)
const error = ref('')
const detail = ref(null)
const detailVisible = ref(false)

async function load() {
  loading.value = true
  error.value = ''
  try {
    items.value = await listPipelines()
  } catch (exception) {
    error.value = exception.message
  } finally {
    loading.value = false
  }
}

async function open(row) {
  try {
    detail.value = await getPipeline(row.taskId)
    detailVisible.value = true
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
        <h2>Pipelines</h2>
        <p class="muted">任务 → 尝试 → Coding Run → Review → QA 只读时间线</p>
      </div>
      <el-button @click="load">刷新</el-button>
    </div>
    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />
    <el-table v-loading="loading" :data="items" empty-text="暂无任务">
      <el-table-column prop="taskCode" label="任务" width="180" />
      <el-table-column prop="title" label="标题" min-width="220" />
      <el-table-column prop="taskStatus" label="状态" width="120" />
      <el-table-column prop="attemptCount" label="尝试" width="80" />
      <el-table-column label="Coding Run" width="110">
        <template #default="{ row }">{{ row.hasCodingRun ? '是' : '否' }}</template>
      </el-table-column>
      <el-table-column label="更新时间" width="180">
        <template #default="{ row }">{{ formatUtc(row.updatedAtUtc) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="100">
        <template #default="{ row }">
          <el-button link type="primary" @click="open(row)">查看</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-drawer v-model="detailVisible" title="Pipeline" size="520px">
      <template v-if="detail">
        <h3>{{ detail.taskCode }} · {{ detail.taskStatus }}</h3>
        <p class="muted">{{ detail.title }}</p>
        <el-timeline>
          <el-timeline-item
            v-for="stage in detail.stages"
            :key="stage.name + stage.status + stage.detail"
            :timestamp="formatUtc(stage.completedAtUtc || stage.startedAtUtc)"
            placement="top"
          >
            <strong>{{ stage.name }}</strong> · {{ stage.status }}
            <div v-if="stage.detail" class="muted">{{ stage.detail }}</div>
          </el-timeline-item>
        </el-timeline>
      </template>
    </el-drawer>
  </div>
</template>
