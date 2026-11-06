<script setup>
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { Refresh } from '@element-plus/icons-vue'
import { downloadArtifact, getAttemptLogs, getTask } from '../api/observability'
import { formatUtc, statusTagType } from '../utils/format'
import { useTaskHub } from '../composables/useTaskHub'

const route = useRoute()
const detail = ref(null)
const loading = ref(false)
const error = ref('')

const logs = ref([])
const nextSequence = ref(null)
const level = ref('')
const logLoading = ref(false)
const levels = ['', 'debug', 'info', 'warn', 'error']
const selectedAttemptId = ref('')

const { start, stop } = useTaskHub(async () => {
  await load()
  await loadLogs(true)
})

async function load() {
  loading.value = true
  error.value = ''
  try {
    detail.value = await getTask(route.params.id)
    if (!selectedAttemptId.value && detail.value.attempts.length) {
      selectedAttemptId.value = detail.value.attempts[detail.value.attempts.length - 1].id
    }
  } catch (exception) {
    error.value = exception.message
  } finally {
    loading.value = false
  }
}

async function loadLogs(reset = false) {
  logLoading.value = true
  try {
    const after = reset ? 0 : nextSequence.value || 0
    if (!selectedAttemptId.value) return
    const page = await getAttemptLogs(route.params.id, selectedAttemptId.value, {
      level: level.value || undefined,
      afterSequence: after,
      limit: 100
    })
    logs.value = reset ? page.items : [...logs.value, ...page.items]
    nextSequence.value = page.nextSequence
  } catch (exception) {
    error.value = exception.message
  } finally {
    logLoading.value = false
  }
}

async function download(row) {
  try {
    await downloadArtifact(row.id, row.name)
  } catch (exception) {
    error.value = exception.message
  }
}

onMounted(async () => {
  await load()
  await loadLogs(true)
  await start()
})

watch(selectedAttemptId, () => loadLogs(true))

onBeforeUnmount(() => stop())
</script>

<template>
  <div v-loading="loading">
    <div class="page-head">
      <div>
        <h2>Task Detail</h2>
        <p class="muted"><router-link to="/tasks">返回任务列表</router-link></p>
      </div>
      <el-button :loading="loading" @click="load">
        <el-icon><Refresh /></el-icon>
        刷新
      </el-button>
    </div>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" class="section" />

    <template v-if="detail">
      <el-descriptions :column="4" border>
        <el-descriptions-item label="任务">{{ detail.task.taskCode }}</el-descriptions-item>
        <el-descriptions-item label="状态">
          <el-tag :type="statusTagType(detail.task.status)" size="small">{{ detail.task.status }}</el-tag>
        </el-descriptions-item>
        <el-descriptions-item label="优先级">{{ detail.task.priority }}</el-descriptions-item>
        <el-descriptions-item label="尝试">{{ detail.task.attemptCount }}/{{ detail.task.maxAttempts }}</el-descriptions-item>
        <el-descriptions-item label="标题" :span="2">{{ detail.task.title }}</el-descriptions-item>
        <el-descriptions-item label="项目">{{ detail.task.projectSlug }}</el-descriptions-item>
        <el-descriptions-item label="Agent / Skill">
          {{ detail.task.agentCode }} / {{ detail.task.skillCode || '—' }}
        </el-descriptions-item>
        <el-descriptions-item label="创建">{{ formatUtc(detail.task.createdAtUtc) }}</el-descriptions-item>
        <el-descriptions-item label="开始">{{ formatUtc(detail.task.startedAtUtc) }}</el-descriptions-item>
        <el-descriptions-item label="完成">{{ formatUtc(detail.task.completedAtUtc) }}</el-descriptions-item>
      </el-descriptions>

      <el-card shadow="never" class="section">
        <template #header>Attempts</template>
        <el-table :data="detail.attempts" empty-text="暂无尝试记录">
          <el-table-column prop="attemptNumber" label="#" width="60" />
          <el-table-column prop="status" label="状态" width="110">
            <template #default="{ row }">
              <el-tag :type="statusTagType(row.status)" size="small">{{ row.status }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="workerCode" label="Runner" width="160" />
          <el-table-column label="开始" width="180">
            <template #default="{ row }">{{ formatUtc(row.startedAtUtc) }}</template>
          </el-table-column>
          <el-table-column label="结束" width="180">
            <template #default="{ row }">{{ formatUtc(row.finishedAtUtc) }}</template>
          </el-table-column>
          <el-table-column prop="exitCode" label="退出码" width="90" />
          <el-table-column prop="durationMs" label="耗时" width="100">
            <template #default="{ row }">{{ row.durationMs ? `${(row.durationMs / 1000).toFixed(1)}s` : '—' }}</template>
          </el-table-column>
          <el-table-column prop="errorCode" label="错误码" min-width="140" />
        </el-table>
      </el-card>

      <el-card shadow="never" class="section">
        <template #header>
          <div class="card-head">
            <div class="log-controls">
              <el-select v-model="selectedAttemptId" size="small" style="width: 220px">
                <el-option
                  v-for="attempt in detail.attempts"
                  :key="attempt.id"
                  :label="`Attempt ${attempt.attemptNumber} · ${attempt.status}`"
                  :value="attempt.id"
                />
              </el-select>
              <el-select v-model="level" size="small" style="width: 130px" @change="loadLogs(true)">
                <el-option v-for="item in levels" :key="item" :label="item || '全部级别'" :value="item" />
              </el-select>
            </div>
          </div>
        </template>
        <div v-loading="logLoading" class="logs">
          <div v-for="log in logs" :key="log.id" class="log-line">
            <span class="log-time">{{ formatUtc(log.createdAtUtc) }}</span>
            <el-tag size="small" :type="log.level === 'error' ? 'danger' : log.level === 'warn' ? 'warning' : 'info'">
              {{ log.level }}
            </el-tag>
            <span class="log-component">{{ log.component }}</span>
            <span class="log-message">{{ log.message }}</span>
          </div>
          <el-empty v-if="!logs.length" description="暂无日志" />
        </div>
        <div class="load-more">
          <el-button v-if="nextSequence" :loading="logLoading" @click="loadLogs(false)">加载更多</el-button>
        </div>
      </el-card>

      <el-card shadow="never" class="section">
        <template #header>Artifacts</template>
        <el-table :data="detail.artifacts" empty-text="暂无产物">
          <el-table-column prop="name" label="名称" min-width="200" />
          <el-table-column prop="mediaType" label="类型" width="160" />
          <el-table-column label="大小" width="110">
            <template #default="{ row }">{{ (row.sizeBytes / 1024).toFixed(1) }} KB</template>
          </el-table-column>
          <el-table-column prop="sha256" label="SHA-256" min-width="220" show-overflow-tooltip />
          <el-table-column label="操作" width="100">
            <template #default="{ row }">
              <el-button link type="primary" @click="download(row)">下载</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-card>

      <el-card shadow="never" class="section">
        <template #header>Usage</template>
        <el-descriptions v-if="detail.usage" :column="4" border>
          <el-descriptions-item label="Provider">{{ detail.usage.provider }}</el-descriptions-item>
          <el-descriptions-item label="模型">{{ detail.usage.model || '—' }}</el-descriptions-item>
          <el-descriptions-item label="输入">{{ detail.usage.inputTokens }}</el-descriptions-item>
          <el-descriptions-item label="输出">{{ detail.usage.outputTokens }}</el-descriptions-item>
          <el-descriptions-item label="缓存输入">{{ detail.usage.cachedInputTokens ?? '—' }}</el-descriptions-item>
          <el-descriptions-item label="总计">{{ detail.usage.totalTokens }}</el-descriptions-item>
          <el-descriptions-item label="来源">{{ detail.usage.source }}</el-descriptions-item>
        </el-descriptions>
        <el-alert v-else title="未提供/估算：Adapter 未上报用量" type="info" :closable="false" />
      </el-card>
    </template>
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
.section {
  margin-top: 16px;
}
.card-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.log-controls {
  display: flex;
  gap: 8px;
}
.logs {
  max-height: 360px;
  overflow: auto;
  background: #0f172a;
  color: #e2e8f0;
  padding: 12px;
  border-radius: 6px;
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 12px;
}
.log-line {
  display: flex;
  gap: 8px;
  padding: 2px 0;
  align-items: baseline;
}
.log-time {
  color: #94a3b8;
  white-space: nowrap;
}
.log-component {
  color: #38bdf8;
  white-space: nowrap;
}
.log-message {
  white-space: pre-wrap;
  word-break: break-word;
}
.load-more {
  margin-top: 12px;
  text-align: right;
}
</style>
