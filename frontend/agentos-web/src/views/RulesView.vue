<script setup>
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Plus, Refresh } from '@element-plus/icons-vue'
import { createRule, disableRule, enableRule, fetchRule, fetchRules, updateRule } from '../api/rules'

const rules = ref([])
const total = ref(0)
const loading = ref(false)
const loadError = ref('')
const page = reactive({ number: 1, size: 20 })
const filters = reactive({ enabled: '', project: '' })

const dialogVisible = ref(false)
const editingId = ref(null)
const saving = ref(false)
const formErrors = ref({})
const form = reactive(emptyForm())

function emptyForm() {
  return {
    name: '',
    project: '',
    sourcePattern: '*',
    eventTypePattern: '*',
    agentCode: '',
    skillCode: '',
    modelProfileCode: '',
    matchJson: '{}',
    dispatchJson: '{"dispatchKey":"default","priority":"normal"}',
    priority: 0,
    maxParallel: 1,
    maxAttempts: 2,
    timeoutSeconds: 1800,
    notificationPolicy: 'none',
    isEnabled: false
  }
}

async function load() {
  loading.value = true
  loadError.value = ''

  try {
    const result = await fetchRules({
      page: page.number,
      pageSize: page.size,
      enabled: filters.enabled === '' ? undefined : filters.enabled,
      project: filters.project || undefined
    })
    rules.value = result.items
    total.value = result.total
  } catch (exception) {
    loadError.value = exception.message
  } finally {
    loading.value = false
  }
}

function openCreate() {
  editingId.value = null
  Object.assign(form, emptyForm())
  formErrors.value = {}
  dialogVisible.value = true
}

async function openEdit(row) {
  try {
    // The list projection has no JSON documents; load the detail so PUT does not wipe
    // matchJson/dispatchJson back to defaults.
    const detail = await fetchRule(row.id)
    editingId.value = detail.id
    formErrors.value = {}
    Object.assign(form, {
      name: detail.name,
      project: detail.projectSlug ?? '',
      sourcePattern: detail.sourcePattern,
      eventTypePattern: detail.eventTypePattern,
      agentCode: detail.agentCode,
      skillCode: detail.skillCode ?? '',
      modelProfileCode: detail.modelProfileCode ?? '',
      matchJson: JSON.stringify(detail.matchJson ?? {}, null, 2),
      dispatchJson: JSON.stringify(detail.dispatchJson ?? {}, null, 2),
      priority: detail.priority,
      maxParallel: detail.maxParallel,
      maxAttempts: detail.maxAttempts,
      timeoutSeconds: detail.timeoutSeconds,
      notificationPolicy: detail.notificationPolicy,
      isEnabled: detail.isEnabled
    })
    dialogVisible.value = true
  } catch (exception) {
    ElMessage.error(exception.message)
  }
}

function buildPayload() {
  const errors = {}

  let matchJson = {}
  try {
    matchJson = form.matchJson.trim() === '' ? {} : JSON.parse(form.matchJson)
  } catch {
    errors.matchJson = ['matchJson 必须是合法 JSON。']
  }

  let dispatchJson = {}
  try {
    dispatchJson = form.dispatchJson.trim() === '' ? {} : JSON.parse(form.dispatchJson)
  } catch {
    errors.dispatchJson = ['dispatchJson 必须是合法 JSON。']
  }

  formErrors.value = errors
  if (Object.keys(errors).length > 0) {
    return null
  }

  return {
    name: form.name,
    project: form.project || null,
    sourcePattern: form.sourcePattern,
    eventTypePattern: form.eventTypePattern,
    agentCode: form.agentCode,
    skillCode: form.skillCode || null,
    modelProfileCode: form.modelProfileCode || null,
    matchJson,
    dispatchJson,
    priority: form.priority,
    maxParallel: form.maxParallel,
    maxAttempts: form.maxAttempts,
    timeoutSeconds: form.timeoutSeconds,
    notificationPolicy: form.notificationPolicy,
    isEnabled: form.isEnabled
  }
}

async function save() {
  const payload = buildPayload()
  if (!payload) {
    return
  }

  saving.value = true

  try {
    if (editingId.value) {
      await updateRule(editingId.value, payload)
    } else {
      await createRule(payload)
    }

    ElMessage.success(editingId.value ? '规则已更新' : '规则已创建')
    dialogVisible.value = false
    await load()
  } catch (exception) {
    if (exception.status === 400 && exception.problem?.errors) {
      formErrors.value = exception.problem.errors
    } else {
      ElMessage.error(exception.message)
    }
  } finally {
    saving.value = false
  }
}

async function toggle(row) {
  try {
    if (row.isEnabled) {
      await disableRule(row.id)
    } else {
      await enableRule(row.id)
    }

    await load()
  } catch (exception) {
    if (exception.status === 400 && exception.problem?.errors) {
      const detail = Object.entries(exception.problem.errors)
        .map(([field, messages]) => `${field}: ${messages.join('；')}`)
        .join('\n')
      ElMessage.error(`无法切换启用状态\n${detail}`)
    } else {
      ElMessage.error(exception.message)
    }
  }
}

onMounted(load)
</script>

<template>
  <div class="rules">
    <div class="page-head">
      <div>
        <h2>Rules</h2>
        <p class="muted">事件规则：匹配 source / eventType / project 后生成任务</p>
      </div>
      <div class="head-actions">
        <el-button :loading="loading" @click="load">
          <el-icon><Refresh /></el-icon>
          刷新
        </el-button>
        <el-button type="primary" @click="openCreate">
          <el-icon><Plus /></el-icon>
          新建规则
        </el-button>
      </div>
    </div>

    <el-alert v-if="loadError" :title="loadError" type="error" show-icon :closable="false" class="block" />

    <el-card shadow="never" class="block">
      <div class="filters">
        <el-select v-model="filters.enabled" placeholder="启用状态" clearable class="filter" @change="load">
          <el-option label="已启用" value="true" />
          <el-option label="已禁用" value="false" />
        </el-select>
        <el-input v-model="filters.project" placeholder="项目 slug" clearable class="filter" @keyup.enter="load" />
        <el-button @click="load">查询</el-button>
      </div>

      <el-table v-loading="loading" :data="rules" stripe>
        <el-table-column prop="name" label="名称" min-width="200">
          <template #default="{ row }">
            <router-link :to="`/rules/${row.id}`">{{ row.name }}</router-link>
          </template>
        </el-table-column>
        <el-table-column prop="projectSlug" label="项目" width="120">
          <template #default="{ row }">{{ row.projectSlug || '全局' }}</template>
        </el-table-column>
        <el-table-column prop="sourcePattern" label="Source" width="140" />
        <el-table-column prop="eventTypePattern" label="Event Type" min-width="180" />
        <el-table-column prop="agentCode" label="Agent" width="120" />
        <el-table-column prop="priority" label="优先级" width="90" />
        <el-table-column prop="version" label="版本" width="80" />
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="row.isEnabled ? 'success' : 'info'">{{ row.isEnabled ? '已启用' : '已禁用' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="180" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button link :type="row.isEnabled ? 'warning' : 'success'" @click="toggle(row)">
              {{ row.isEnabled ? '禁用' : '启用' }}
            </el-button>
          </template>
        </el-table-column>
        <template #empty>
          <p class="muted">还没有规则。先创建 Agent / Skill / Model Profile 的引用数据，再新建规则。</p>
        </template>
      </el-table>

      <el-pagination
        v-model:current-page="page.number"
        v-model:page-size="page.size"
        :total="total"
        :page-sizes="[10, 20, 50]"
        layout="total, sizes, prev, pager, next"
        class="pager"
        @change="load"
      />
    </el-card>

    <el-dialog v-model="dialogVisible" :title="editingId ? '编辑规则' : '新建规则'" width="760px" destroy-on-close>
      <el-form label-width="140px">
        <el-form-item label="名称" :error="formErrors.name?.[0]">
          <el-input v-model="form.name" maxlength="120" />
        </el-form-item>
        <el-form-item label="项目 slug" :error="formErrors.project?.[0]">
          <el-input v-model="form.project" placeholder="留空表示全局规则" />
        </el-form-item>
        <el-form-item label="Source 匹配" :error="formErrors.sourcePattern?.[0]">
          <el-input v-model="form.sourcePattern" placeholder="3derp 或 github.*" />
        </el-form-item>
        <el-form-item label="Event Type 匹配" :error="formErrors.eventTypePattern?.[0]">
          <el-input v-model="form.eventTypePattern" placeholder="development.task.*" />
        </el-form-item>
        <el-form-item label="Agent code" :error="formErrors.agentCode?.[0]">
          <el-input v-model="form.agentCode" placeholder="planner" />
        </el-form-item>
        <el-form-item label="Skill code" :error="formErrors.skillCode?.[0]">
          <el-input v-model="form.skillCode" placeholder="可空" />
        </el-form-item>
        <el-form-item label="Model Profile" :error="formErrors.modelProfileCode?.[0]">
          <el-input v-model="form.modelProfileCode" placeholder="可空，例如 strong-reasoning" />
        </el-form-item>
        <el-form-item label="matchJson" :error="formErrors.matchJson?.[0]">
          <el-input v-model="form.matchJson" type="textarea" :rows="3" />
        </el-form-item>
        <el-form-item label="dispatchJson" :error="formErrors.dispatchJson?.[0]">
          <el-input v-model="form.dispatchJson" type="textarea" :rows="3" />
        </el-form-item>
        <el-form-item label="优先级" :error="formErrors.priority?.[0]">
          <el-input-number v-model="form.priority" :min="-1000" :max="1000" />
        </el-form-item>
        <el-form-item label="最大并发" :error="formErrors.maxParallel?.[0]">
          <el-input-number v-model="form.maxParallel" :min="1" :max="64" />
        </el-form-item>
        <el-form-item label="最大尝试次数" :error="formErrors.maxAttempts?.[0]">
          <el-input-number v-model="form.maxAttempts" :min="1" :max="10" />
        </el-form-item>
        <el-form-item label="超时（秒）" :error="formErrors.timeoutSeconds?.[0]">
          <el-input-number v-model="form.timeoutSeconds" :min="10" :max="86400" />
        </el-form-item>
        <el-form-item label="通知策略" :error="formErrors.notificationPolicy?.[0]">
          <el-select v-model="form.notificationPolicy">
            <el-option label="none" value="none" />
            <el-option label="failure" value="failure" />
            <el-option label="failure-or-approval" value="failure-or-approval" />
            <el-option label="always" value="always" />
          </el-select>
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.isEnabled" />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="save">保存</el-button>
      </template>
    </el-dialog>
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

.head-actions {
  display: flex;
  gap: 8px;
}

.filters {
  display: flex;
  gap: 8px;
  margin-bottom: 12px;
}

.filter {
  width: 180px;
}

.pager {
  margin-top: 12px;
  justify-content: flex-end;
}
</style>
