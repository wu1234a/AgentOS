<script setup>
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import {
  createModelProfile,
  disableModelProfile,
  enableModelProfile,
  listModelProfiles,
  updateModelProfile
} from '../api/modelProfiles'
import { formatUtc, statusTagType } from '../utils/format'

const items = ref([])
const loading = ref(false)
const error = ref('')
const dialogVisible = ref(false)
const editing = ref(null)
const form = reactive({
  code: '',
  provider: 'codex-cli',
  model: '',
  executionMode: 'cli',
  configurationJson: '{}',
  secretReference: '',
  timeoutSeconds: 900,
  maxCostPerTask: ''
})

async function load() {
  loading.value = true
  error.value = ''
  try {
    const data = await listModelProfiles()
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
    provider: 'codex-cli',
    model: '',
    executionMode: 'cli',
    configurationJson: '{}',
    secretReference: '',
    timeoutSeconds: 900,
    maxCostPerTask: ''
  })
  dialogVisible.value = true
}

function openEdit(row) {
  editing.value = row
  Object.assign(form, {
    code: row.code,
    provider: row.provider,
    model: row.model,
    executionMode: row.executionMode,
    configurationJson: row.configurationJson,
    secretReference: row.secretReference || '',
    timeoutSeconds: row.timeoutSeconds,
    maxCostPerTask: row.maxCostPerTask ?? ''
  })
  dialogVisible.value = true
}

async function save() {
  try {
    const payload = {
      provider: form.provider,
      model: form.model,
      executionMode: form.executionMode,
      configurationJson: form.configurationJson,
      secretReference: form.secretReference || null,
      timeoutSeconds: Number(form.timeoutSeconds),
      maxCostPerTask: form.maxCostPerTask === '' ? null : Number(form.maxCostPerTask)
    }

    if (editing.value) {
      await updateModelProfile(editing.value.id, payload)
      ElMessage.success('Model Profile 已更新')
    } else {
      await createModelProfile({ ...payload, code: form.code })
      ElMessage.success('Model Profile 已创建')
    }

    dialogVisible.value = false
    await load()
  } catch (exception) {
    ElMessage.error(exception.message)
  }
}

async function toggle(row) {
  try {
    if (row.isEnabled) {
      await disableModelProfile(row.id)
    } else {
      await enableModelProfile(row.id)
    }
    await load()
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
        <h2>Model Profiles</h2>
        <p class="muted">模型 / Provider 配置与执行参数（密钥仅存环境变量引用）</p>
      </div>
      <el-button type="primary" @click="openCreate">新建 Profile</el-button>
    </div>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />

    <el-table v-loading="loading" :data="items" empty-text="暂无 Model Profile">
      <el-table-column prop="code" label="Code" min-width="170" />
      <el-table-column prop="provider" label="Provider" width="140" />
      <el-table-column prop="model" label="Model" min-width="180" />
      <el-table-column prop="executionMode" label="模式" width="90" />
      <el-table-column prop="secretReference" label="密钥引用" min-width="160">
        <template #default="{ row }">{{ row.secretReference || '—' }}</template>
      </el-table-column>
      <el-table-column prop="timeoutSeconds" label="超时(s)" width="100" />
      <el-table-column label="状态" width="100">
        <template #default="{ row }">
          <el-tag :type="statusTagType(row.isEnabled ? 'Online' : 'Disabled')" size="small">
            {{ row.isEnabled ? 'Enabled' : 'Disabled' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="更新时间" width="180">
        <template #default="{ row }">{{ formatUtc(row.updatedAtUtc) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="160">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          <el-button link :type="row.isEnabled ? 'warning' : 'success'" @click="toggle(row)">
            {{ row.isEnabled ? '禁用' : '启用' }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dialogVisible" :title="editing ? '编辑 Model Profile' : '新建 Model Profile'" width="560px">
      <el-form label-width="130px">
        <el-form-item label="Code">
          <el-input v-model="form.code" :disabled="!!editing" placeholder="小写 slug，例如 codex-default" />
        </el-form-item>
        <el-form-item label="Provider">
          <el-input v-model="form.provider" placeholder="codex-cli / openai / kimi" />
        </el-form-item>
        <el-form-item label="Model">
          <el-input v-model="form.model" placeholder="模型 ID" />
        </el-form-item>
        <el-form-item label="执行模式">
          <el-select v-model="form.executionMode">
            <el-option label="cli" value="cli" />
            <el-option label="api" value="api" />
          </el-select>
        </el-form-item>
        <el-form-item label="Configuration">
          <el-input v-model="form.configurationJson" type="textarea" :rows="4" />
        </el-form-item>
        <el-form-item label="密钥环境变量">
          <el-input v-model="form.secretReference" placeholder="例如 OPENAI_API_KEY，留空表示无" />
        </el-form-item>
        <el-form-item label="超时(秒)">
          <el-input v-model="form.timeoutSeconds" type="number" />
        </el-form-item>
        <el-form-item label="单任务成本上限">
          <el-input v-model="form.maxCostPerTask" type="number" placeholder="可留空" />
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
.muted {
  color: #6b7280;
  margin: 4px 0 0;
}
</style>
