<script setup>
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import {
  createSkill,
  disableSkill,
  enableSkill,
  getSkillContent,
  listSkills
} from '../api/skills'
import { formatUtc, statusTagType, truncate } from '../utils/format'

const items = ref([])
const loading = ref(false)
const error = ref('')
const codeFilter = ref('')
const includeDisabled = ref(false)

const dialogVisible = ref(false)
const contentVisible = ref(false)
const content = ref('')
const contentTitle = ref('')
const form = reactive({ code: '', version: '', description: '', content: '' })

async function load() {
  loading.value = true
  error.value = ''
  try {
    const data = await listSkills({
      code: codeFilter.value || undefined,
      includeDisabled: includeDisabled.value
    })
    items.value = data.items
  } catch (exception) {
    error.value = exception.message
  } finally {
    loading.value = false
  }
}

function openCreate() {
  Object.assign(form, { code: '', version: '', description: '', content: '' })
  dialogVisible.value = true
}

async function save() {
  try {
    await createSkill({
      code: form.code,
      version: form.version,
      description: form.description || null,
      content: form.content
    })
    ElMessage.success('Skill 版本已创建')
    dialogVisible.value = false
    await load()
  } catch (exception) {
    ElMessage.error(exception.message)
  }
}

async function viewContent(row) {
  try {
    content.value = await getSkillContent(row.id)
    contentTitle.value = `${row.code} @ ${row.version}`
    contentVisible.value = true
  } catch (exception) {
    ElMessage.error(exception.message)
  }
}

async function toggle(row) {
  try {
    if (row.isEnabled) {
      await disableSkill(row.id)
    } else {
      await enableSkill(row.id)
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
        <h2>Skills</h2>
        <p class="muted">版本化 SOP 与 Markdown 内容</p>
      </div>
      <div class="actions">
        <el-input v-model="codeFilter" placeholder="按 Code 过滤" style="width: 200px" @keyup.enter="load" />
        <el-checkbox v-model="includeDisabled" @change="load">包含禁用</el-checkbox>
        <el-button type="primary" @click="openCreate">新建版本</el-button>
      </div>
    </div>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />

    <el-table v-loading="loading" :data="items" empty-text="暂无 Skill">
      <el-table-column prop="code" label="Code" min-width="150" />
      <el-table-column prop="version" label="版本" width="100" />
      <el-table-column prop="projectSlug" label="项目" width="140">
        <template #default="{ row }">{{ row.projectSlug || '全局' }}</template>
      </el-table-column>
      <el-table-column prop="description" label="描述" min-width="220" show-overflow-tooltip />
      <el-table-column label="Hash" width="150">
        <template #default="{ row }">{{ truncate(row.contentHash, 16) }}</template>
      </el-table-column>
      <el-table-column label="状态" width="100">
        <template #default="{ row }">
          <el-tag :type="statusTagType(row.isEnabled ? 'Online' : 'Disabled')" size="small">
            {{ row.isEnabled ? 'Enabled' : 'Disabled' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="创建时间" width="180">
        <template #default="{ row }">{{ formatUtc(row.createdAtUtc) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="210">
        <template #default="{ row }">
          <el-button link type="primary" @click="viewContent(row)">查看</el-button>
          <el-button link :type="row.isEnabled ? 'warning' : 'success'" @click="toggle(row)">
            {{ row.isEnabled ? '禁用' : '启用' }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dialogVisible" title="新建 Skill 版本" width="680px">
      <el-form label-width="100px">
        <el-form-item label="Code">
          <el-input v-model="form.code" placeholder="小写 slug，例如 bugfix-sop" />
        </el-form-item>
        <el-form-item label="版本">
          <el-input v-model="form.version" placeholder="例如 1.0.0" />
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="form.description" maxlength="1000" />
        </el-form-item>
        <el-form-item label="Markdown">
          <el-input v-model="form.content" type="textarea" :rows="12" placeholder="# SOP..." />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="save">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="contentVisible" :title="contentTitle" width="680px">
      <pre class="markdown">{{ content }}</pre>
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
.markdown {
  white-space: pre-wrap;
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 12px;
  background: #0f172a;
  color: #e2e8f0;
  padding: 12px;
  border-radius: 6px;
  max-height: 480px;
  overflow: auto;
}
</style>
