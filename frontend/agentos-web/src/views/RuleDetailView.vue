<script setup>
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Back } from '@element-plus/icons-vue'
import { disableRule, enableRule, fetchRule } from '../api/rules'

const route = useRoute()
const router = useRouter()
const rule = ref(null)
const loading = ref(false)
const error = ref('')

async function load() {
  loading.value = true
  error.value = ''

  try {
    rule.value = await fetchRule(route.params.id)
  } catch (exception) {
    error.value = exception.message
  } finally {
    loading.value = false
  }
}

async function toggle() {
  try {
    if (rule.value.isEnabled) {
      rule.value = await disableRule(rule.value.id)
    } else {
      rule.value = await enableRule(rule.value.id)
    }

    ElMessage.success('状态已更新')
  } catch (exception) {
    if (exception.status === 400 && exception.problem?.errors) {
      const detail = Object.entries(exception.problem.errors)
        .map(([field, messages]) => `${field}: ${messages.join('；')}`)
        .join('\n')
      ElMessage.error(`无法启用规则\n${detail}`)
    } else {
      ElMessage.error(exception.message)
    }
  }
}

onMounted(load)
</script>

<template>
  <div class="rule-detail">
    <div class="page-head">
      <div>
        <h2>{{ rule?.name || '规则详情' }}</h2>
        <p class="muted">规则决定路由，不执行 Agent；每次任务都会记录 RuleId 与 RuleVersion</p>
      </div>
      <div class="actions">
        <el-button @click="router.push('/rules')">
          <el-icon><Back /></el-icon>
          返回列表
        </el-button>
        <el-button v-if="rule" :type="rule.isEnabled ? 'warning' : 'success'" @click="toggle">
          {{ rule.isEnabled ? '禁用' : '启用' }}
        </el-button>
      </div>
    </div>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" class="block" />

    <el-card v-loading="loading" shadow="never" class="block">
      <template v-if="rule">
        <el-descriptions :column="2" border>
          <el-descriptions-item label="ID">{{ rule.id }}</el-descriptions-item>
          <el-descriptions-item label="状态">
            <el-tag :type="rule.isEnabled ? 'success' : 'info'">{{ rule.isEnabled ? '已启用' : '已禁用' }}</el-tag>
          </el-descriptions-item>
          <el-descriptions-item label="项目">{{ rule.projectSlug || '全局' }}</el-descriptions-item>
          <el-descriptions-item label="版本">{{ rule.version }}</el-descriptions-item>
          <el-descriptions-item label="Source 匹配">{{ rule.sourcePattern }}</el-descriptions-item>
          <el-descriptions-item label="Event Type 匹配">{{ rule.eventTypePattern }}</el-descriptions-item>
          <el-descriptions-item label="Agent">{{ rule.agentCode }}</el-descriptions-item>
          <el-descriptions-item label="Skill">{{ rule.skillCode || '—' }}</el-descriptions-item>
          <el-descriptions-item label="Model Profile">{{ rule.modelProfileCode || '—' }}</el-descriptions-item>
          <el-descriptions-item label="优先级">{{ rule.priority }}</el-descriptions-item>
          <el-descriptions-item label="最大并发">{{ rule.maxParallel }}</el-descriptions-item>
          <el-descriptions-item label="最大尝试次数">{{ rule.maxAttempts }}</el-descriptions-item>
          <el-descriptions-item label="超时（秒）">{{ rule.timeoutSeconds }}</el-descriptions-item>
          <el-descriptions-item label="通知策略">{{ rule.notificationPolicy }}</el-descriptions-item>
          <el-descriptions-item label="创建时间">{{ rule.createdAtUtc }}</el-descriptions-item>
          <el-descriptions-item label="更新时间">{{ rule.updatedAtUtc }}</el-descriptions-item>
        </el-descriptions>

        <h3 class="section-title">matchJson</h3>
        <pre class="json">{{ JSON.stringify(rule.matchJson, null, 2) }}</pre>

        <h3 class="section-title">dispatchJson</h3>
        <pre class="json">{{ JSON.stringify(rule.dispatchJson, null, 2) }}</pre>
      </template>
      <p v-else-if="!loading && !error" class="muted">规则不存在。</p>
    </el-card>
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

.block {
  margin-bottom: 16px;
}

.actions {
  display: flex;
  gap: 8px;
}

.section-title {
  margin: 20px 0 8px;
  font-size: 14px;
}

.json {
  background: #111827;
  color: #e5e7eb;
  padding: 12px 16px;
  border-radius: 6px;
  overflow: auto;
  font-size: 13px;
  margin: 0;
}
</style>
