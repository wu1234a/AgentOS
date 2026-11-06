<script setup>
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  archiveAgent,
  createAgent,
  disableAgent,
  enableAgent,
  listAgents,
  updateAgent
} from '../api/agents'
import { formatUtc, statusTagType } from '../utils/format'

const items = ref([])
const loading = ref(false)
const error = ref('')
const role = ref('')
const includeArchived = ref(false)
const roles = ['', 'planner', 'coder', 'reviewer', 'qa']
const permissions = ['read-only', 'workspace-write']

const dialogVisible = ref(false)
const editing = ref(null)
const form = reactive({
  code: '',
  displayName: '',
  role: 'coder',
  permissionProfile: 'read-only',
  description: '',
  systemPromptPath: ''
})

async function load() {
  loading.value = true
  error.value = ''
  try {
    const data = await listAgents({ role: role.value || undefined, includeArchived: includeArchived.value })
    items.value = data.items
  } catch (exception) {
    error.value = exception.message
  } finally {
    loading.value = false
  }
}

function openCreate() {
  editing.value = null
  Object.assign(form, {
    code: '',
    displayName: '',
    role: 'coder',
    permissionProfile: 'read-only',
    description: '',
    systemPromptPath: ''
  })
  dialogVisible.value = true
}

function openEdit(row) {
  editing.value = row
  Object.assign(form, {
    code: row.code,
    displayName: row.displayName,
    role: row.role,
    permissionProfile: row.permissionProfile,
    description: row.description || '',
    systemPromptPath: row.systemPromptPath || ''
  })
  dialogVisible.value = true
}

async function save() {
  try {
    const payload = {
      displayName: form.displayName,
      role: form.role,
      permissionProfile: form.permissionProfile,
      description: form.description || null,
      systemPromptPath: form.systemPromptPath || null
    }

    if (editing.value) {
      await updateAgent(editing.value.id, payload)
      ElMessage.success('Agent 已更新')
    } else {
      await createAgent({ ...payload, code: form.code })
      ElMessage.success('Agent 已创建')
    }

    dialogVisible.value = false
    await load()
  } catch (exception) {
    ElMessage.error(exception.message)
  }
}

async function changeState(row, action) {
  const label = { enable: '启用', disable: '禁用', archive: '归档' }[action]
  try {
    await ElMessageBox.confirm(`确定${label} Agent ${row.code}？`, '确认', { type: 'warning' })
    if (action === 'enable') await enableAgent(row.id)
    if (action === 'disable') await disableAgent(row.id)
    if (action === 'archive') await archiveAgent(row.id)
    ElMessage.success(`已${label}`)
    await load()
  } catch (exception) {
    if (exception !== 'cancel') ElMessage.error(exception.message)
  }
}

function lifecycle(row) {
  if (row.archivedAtUtc) return 'Archived'
  return row.isEnabled ? 'Active' : 'Disabled'
}

onMounted(load)
</script>

<template>
  <div>
    <div class="page-head">
      <div>
        <h2>Agents</h2>
        <p class="muted">Agent 注册中心与生命周期</p>
      </div>
      <div class="actions">
        <el-select v-model="role" placeholder="全部角色" style="width: 150px" @change="load">
          <el-option v-for="item in roles" :key="item" :label="item || '全部角色'" :value="item" />
        </el-select>
        <el-checkbox v-model="includeArchived" @change="load">包含归档</el-checkbox>
        <el-button type="primary" @click="openCreate">新建 Agent</el-button>
      </div>
    </div>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />

    <el-table v-loading="loading" :data="items" empty-text="暂无 Agent">
      <el-table-column prop="code" label="Code" min-width="160" />
      <el-table-column prop="displayName" label="名称" min-width="160" />
      <el-table-column prop="role" label="角色" width="110" />
      <el-table-column prop="permissionProfile" label="权限" width="150" />
      <el-table-column prop="projectSlug" label="项目" width="140">
        <template #default="{ row }">{{ row.projectSlug || '全局' }}</template>
      </el-table-column>
      <el-table-column label="状态" width="110">
        <template #default="{ row }">
          <el-tag :type="statusTagType(lifecycle(row))" size="small">{{ lifecycle(row) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="更新时间" width="180">
        <template #default="{ row }">{{ formatUtc(row.updatedAtUtc) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="260">
        <template #default="{ row }">
          <el-button link type="primary" :disabled="!!row.archivedAtUtc" @click="openEdit(row)">编辑</el-button>
          <el-button
            v-if="row.isEnabled && !row.archivedAtUtc"
            link
            type="warning"
            @click="changeState(row, 'disable')"
          >
            禁用
          </el-button>
          <el-button
            v-else-if="!row.archivedAtUtc"
            link
            type="success"
            @click="changeState(row, 'enable')"
          >
            启用
          </el-button>
          <el-button
            link
            type="danger"
            :disabled="!!row.archivedAtUtc"
            @click="changeState(row, 'archive')"
          >
            归档
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dialogVisible" :title="editing ? '编辑 Agent' : '新建 Agent'" width="520px">
      <el-form label-width="120px">
        <el-form-item label="Code">
          <el-input v-model="form.code" :disabled="!!editing" placeholder="小写 slug，例如 agent-coder" />
        </el-form-item>
        <el-form-item label="名称">
          <el-input v-model="form.displayName" maxlength="120" />
        </el-form-item>
        <el-form-item label="角色">
          <el-select v-model="form.role">
            <el-option v-for="item in roles.slice(1)" :key="item" :label="item" :value="item" />
          </el-select>
        </el-form-item>
        <el-form-item label="权限">
          <el-select v-model="form.permissionProfile">
            <el-option v-for="item in permissions" :key="item" :label="item" :value="item" />
          </el-select>
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="form.description" type="textarea" :rows="2" maxlength="1000" />
        </el-form-item>
        <el-form-item label="Prompt 路径">
          <el-input v-model="form.systemPromptPath" placeholder="相对路径，可留空" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="save">保存</el-button>
      </template>
    </el-dialog>
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
  align-items: center;
  gap: 12px;
}
.muted {
  color: #6b7280;
  margin: 4px 0 0;
}
</style>
