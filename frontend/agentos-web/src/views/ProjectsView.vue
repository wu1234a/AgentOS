<script setup>
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import {
  createProject,
  listProjects,
  setProjectEnabled,
  suggestProject,
  updateProject
} from '../api/projects'
import { getProjectDocument, listProjectDocuments } from '../api/projectDocuments'

const items = ref([])
const loading = ref(false)
const error = ref('')
const createVisible = ref(false)
const detailVisible = ref(false)
const detail = ref(null)
const documents = ref([])
const documentVisible = ref(false)
const documentDetail = ref(null)
const documentCompareVisible = ref(false)
const documentCompareTitle = ref('')
const documentCompareLines = ref([])
const suggestVisible = ref(false)
const suggesting = ref(false)

const form = reactive({
  name: '',
  slug: '',
  goal: '',
  brief: '',
  personalNotes: '',
  acceptanceCriteria: '',
  repository: '',
  status: 'Active',
  maxConcurrentTasks: 2
})

const suggest = reactive({
  goal: '',
  notes: '',
  repository: ''
})

const edit = reactive({
  visible: false,
  id: '',
  name: '',
  goal: '',
  brief: '',
  personalNotes: '',
  acceptanceCriteria: '',
  repository: '',
  status: 'Active',
  maxConcurrentTasks: 2
})

const statuses = ['Draft', 'Active', 'OnHold', 'Completed', 'Archived']

function resetForm() {
  Object.assign(form, {
    name: '',
    slug: '',
    goal: '',
    brief: '',
    personalNotes: '',
    acceptanceCriteria: '',
    repository: '',
    status: 'Active',
    maxConcurrentTasks: 2
  })
}

function statusType(status) {
  switch (status) {
    case 'Active':
      return 'success'
    case 'Completed':
      return 'primary'
    case 'OnHold':
      return 'warning'
    case 'Archived':
      return 'info'
    default:
      return ''
  }
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    items.value = await listProjects()
  } catch (exception) {
    error.value = exception.message
  } finally {
    loading.value = false
  }
}

function openCreate() {
  resetForm()
  createVisible.value = true
}

async function save() {
  try {
    await createProject({ ...form, isEnabled: true })
    ElMessage.success('项目已创建')
    createVisible.value = false
    await load()
  } catch (exception) {
    ElMessage.error(exception.message)
  }
}

function openSuggest() {
  Object.assign(suggest, { goal: '', notes: '', repository: '' })
  suggestVisible.value = true
}

async function buildSuggestion() {
  if (suggest.goal.trim().length < 10) {
    ElMessage.warning('目标至少 10 个字符')
    return
  }

  try {
    suggesting.value = true
    const suggestion = await suggestProject({
      goal: suggest.goal,
      notes: suggest.notes || null,
      repository: suggest.repository || null
    })
    Object.assign(form, {
      name: suggestion.name,
      slug: suggestion.slug,
      goal: suggestion.goal,
      brief: suggestion.brief,
      personalNotes: suggest.notes,
      acceptanceCriteria: suggestion.acceptanceCriteria,
      repository: suggestion.repository || '',
      status: suggestion.status || 'Draft',
      maxConcurrentTasks: 2
    })
    suggestVisible.value = false
    createVisible.value = true
  } catch (exception) {
    ElMessage.error(exception.message)
  } finally {
    suggesting.value = false
  }
}

async function openDetail(row) {
  detail.value = row
  detailVisible.value = true
  try {
    documents.value = await listProjectDocuments(row.id)
  } catch {
    documents.value = []
  }
}

async function openDocument(doc) {
  try {
    documentDetail.value = await getProjectDocument(detail.value.id, doc.id)
    documentVisible.value = true
  } catch (exception) {
    ElMessage.error(exception.message)
  }
}

function previousDocument(doc) {
  return documents.value.find(
    (candidate) => candidate.documentType === doc.documentType && candidate.version === doc.version - 1
  )
}

async function compareDocument(doc) {
  const previous = previousDocument(doc)
  if (!previous) {
    ElMessage.info('没有上一版本可对比')
    return
  }

  try {
    const [before, after] = await Promise.all([
      getProjectDocument(detail.value.id, previous.id),
      getProjectDocument(detail.value.id, doc.id)
    ])
    documentCompareTitle.value = `${doc.title} v${previous.version} → v${doc.version}`
    documentCompareLines.value = diffLines(before.contentMarkdown, after.contentMarkdown)
    documentCompareVisible.value = true
  } catch (exception) {
    ElMessage.error(exception.message)
  }
}

function diffLines(beforeText, afterText) {
  const before = (beforeText || '').split('\n')
  const after = (afterText || '').split('\n')
  if (before.length * after.length > 400000) {
    return [
      ...before.map((text) => ({ type: 'same', text })),
      ...after.map((text) => ({ type: 'add', text }))
    ]
  }

  const dp = Array.from({ length: before.length + 1 }, () => new Array(after.length + 1).fill(0))
  for (let i = before.length - 1; i >= 0; i--) {
    for (let j = after.length - 1; j >= 0; j--) {
      dp[i][j] = before[i] === after[j] ? dp[i + 1][j + 1] + 1 : Math.max(dp[i + 1][j], dp[i][j + 1])
    }
  }

  const lines = []
  let i = 0
  let j = 0
  while (i < before.length && j < after.length) {
    if (before[i] === after[j]) {
      lines.push({ type: 'same', text: before[i] })
      i++
      j++
    } else if (dp[i + 1][j] >= dp[i][j + 1]) {
      lines.push({ type: 'remove', text: before[i] })
      i++
    } else {
      lines.push({ type: 'add', text: after[j] })
      j++
    }
  }

  while (i < before.length) lines.push({ type: 'remove', text: before[i++] })
  while (j < after.length) lines.push({ type: 'add', text: after[j++] })
  return lines
}

function openEdit(row) {
  Object.assign(edit, {
    visible: true,
    id: row.id,
    name: row.name,
    goal: row.goal || '',
    brief: row.brief || '',
    personalNotes: row.personalNotes || '',
    acceptanceCriteria: row.acceptanceCriteria || '',
    repository: row.repository || '',
    status: row.status || 'Active',
    maxConcurrentTasks: row.maxConcurrentTasks || 1
  })
}

async function saveEdit() {
  try {
    await updateProject(edit.id, {
      name: edit.name,
      goal: edit.goal,
      brief: edit.brief,
      personalNotes: edit.personalNotes,
      acceptanceCriteria: edit.acceptanceCriteria,
      repository: edit.repository,
      status: edit.status,
      maxConcurrentTasks: edit.maxConcurrentTasks
    })
    ElMessage.success('项目已更新')
    edit.visible = false
    await load()
  } catch (exception) {
    ElMessage.error(exception.message)
  }
}

async function toggle(row) {
  try {
    await setProjectEnabled(row.id, !row.isEnabled)
    await load()
  } catch (exception) {
    ElMessage.error(exception.message)
  }
}

onMounted(load)
</script>

<template>
  <div data-testid="projects-page">
    <div class="page-head">
      <div>
        <h2>Projects</h2>
        <p class="muted">项目目标、简报、验收标准、配额与启停</p>
      </div>
      <div class="actions">
        <el-button @click="openSuggest">从目标创建</el-button>
        <el-button type="primary" data-testid="project-create" @click="openCreate">注册项目</el-button>
      </div>
    </div>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />

    <el-table v-loading="loading" :data="items" empty-text="暂无项目" data-testid="projects-table">
      <el-table-column prop="name" label="名称" min-width="160">
        <template #default="{ row }">
          <el-button link type="primary" @click="openDetail(row)">{{ row.name }}</el-button>
        </template>
      </el-table-column>
      <el-table-column prop="slug" label="Slug" min-width="120" />
      <el-table-column prop="goal" label="目标" min-width="260" show-overflow-tooltip>
        <template #default="{ row }">{{ row.goal || '—' }}</template>
      </el-table-column>
      <el-table-column label="项目状态" width="110">
        <template #default="{ row }">
          <el-tag :type="statusType(row.status)">{{ row.status || 'Active' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="开关" width="90">
        <template #default="{ row }">
          <el-tag :type="row.isEnabled ? 'success' : 'info'">{{ row.isEnabled ? '启用' : '停用' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="repository" label="仓库" min-width="150">
        <template #default="{ row }">{{ row.repository || '—' }}</template>
      </el-table-column>
      <el-table-column label="操作" width="210" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="openDetail(row)">详情</el-button>
          <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          <el-button link :type="row.isEnabled ? 'warning' : 'success'" @click="toggle(row)">
            {{ row.isEnabled ? '停用' : '启用' }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="createVisible" title="注册项目" width="760px">
      <el-form label-width="110px">
        <el-form-item label="名称"><el-input v-model="form.name" data-testid="project-name" /></el-form-item>
        <el-form-item label="Slug"><el-input v-model="form.slug" placeholder="lowercase-slug" data-testid="project-slug" /></el-form-item>
        <el-form-item label="一句话目标"><el-input v-model="form.goal" type="textarea" :rows="2" maxlength="2000" /></el-form-item>
        <el-form-item label="项目简报">
          <el-input v-model="form.brief" type="textarea" :rows="5" maxlength="8000" placeholder="背景、范围、约束、技术栈、不做什么" />
        </el-form-item>
        <el-form-item label="个人想法">
          <el-input v-model="form.personalNotes" type="textarea" :rows="3" maxlength="8000" />
        </el-form-item>
        <el-form-item label="验收标准">
          <el-input v-model="form.acceptanceCriteria" type="textarea" :rows="3" maxlength="8000" />
        </el-form-item>
        <el-form-item label="仓库"><el-input v-model="form.repository" placeholder="owner/repo" /></el-form-item>
        <el-form-item label="项目状态">
          <el-select v-model="form.status" style="width: 180px">
            <el-option v-for="item in statuses" :key="item" :label="item" :value="item" />
          </el-select>
        </el-form-item>
        <el-form-item label="并发配额"><el-input-number v-model="form.maxConcurrentTasks" :min="1" :max="10000" data-testid="project-quota" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false">取消</el-button>
        <el-button type="primary" data-testid="project-save" @click="save">创建</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="suggestVisible" title="从目标创建项目" width="620px">
      <p class="muted">先写目标，系统生成项目草稿；你确认后才会真正创建。</p>
      <el-form label-width="100px">
        <el-form-item label="目标">
          <el-input v-model="suggest.goal" type="textarea" :rows="4" maxlength="2000" placeholder="例如：为 3dERP 增加只读报表模块，首期库存，预算有限" />
        </el-form-item>
        <el-form-item label="个人想法">
          <el-input v-model="suggest.notes" type="textarea" :rows="3" maxlength="8000" />
        </el-form-item>
        <el-form-item label="仓库"><el-input v-model="suggest.repository" placeholder="owner/repo（可选）" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="suggestVisible = false">取消</el-button>
        <el-button type="primary" :loading="suggesting" @click="buildSuggestion">生成项目草稿</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="edit.visible" title="编辑项目" width="760px">
      <el-form label-width="110px">
        <el-form-item label="名称"><el-input v-model="edit.name" /></el-form-item>
        <el-form-item label="一句话目标"><el-input v-model="edit.goal" type="textarea" :rows="2" maxlength="2000" /></el-form-item>
        <el-form-item label="项目简报"><el-input v-model="edit.brief" type="textarea" :rows="5" maxlength="8000" /></el-form-item>
        <el-form-item label="个人想法"><el-input v-model="edit.personalNotes" type="textarea" :rows="3" maxlength="8000" /></el-form-item>
        <el-form-item label="验收标准"><el-input v-model="edit.acceptanceCriteria" type="textarea" :rows="3" maxlength="8000" /></el-form-item>
        <el-form-item label="仓库"><el-input v-model="edit.repository" /></el-form-item>
        <el-form-item label="项目状态">
          <el-select v-model="edit.status" style="width: 180px">
            <el-option v-for="item in statuses" :key="item" :label="item" :value="item" />
          </el-select>
        </el-form-item>
        <el-form-item label="并发配额"><el-input-number v-model="edit.maxConcurrentTasks" :min="1" :max="10000" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="edit.visible = false">取消</el-button>
        <el-button type="primary" @click="saveEdit">保存</el-button>
      </template>
    </el-dialog>

    <el-drawer v-model="detailVisible" title="项目详情" size="62%">
      <template v-if="detail">
        <div class="detail-head">
          <div>
            <h3>{{ detail.name }}</h3>
            <div class="muted">{{ detail.slug }}</div>
          </div>
          <el-tag :type="statusType(detail.status)" size="large">{{ detail.status || 'Active' }}</el-tag>
        </div>
        <el-descriptions :column="2" border>
          <el-descriptions-item label="目标" :span="2">{{ detail.goal || '—' }}</el-descriptions-item>
          <el-descriptions-item label="仓库">{{ detail.repository || '—' }}</el-descriptions-item>
          <el-descriptions-item label="启用">{{ detail.isEnabled ? '是' : '否' }}</el-descriptions-item>
          <el-descriptions-item label="并发配额">{{ detail.maxConcurrentTasks ?? '不限' }}</el-descriptions-item>
          <el-descriptions-item label="更新时间">{{ detail.updatedAtUtc }}</el-descriptions-item>
          <el-descriptions-item label="简报" :span="2">
            <pre class="detail-pre">{{ detail.brief || '—' }}</pre>
          </el-descriptions-item>
          <el-descriptions-item label="个人想法" :span="2">
            <pre class="detail-pre">{{ detail.personalNotes || '—' }}</pre>
          </el-descriptions-item>
          <el-descriptions-item label="验收标准" :span="2">
            <pre class="detail-pre">{{ detail.acceptanceCriteria || '—' }}</pre>
          </el-descriptions-item>
        </el-descriptions>

        <div class="documents-head">
          <h4>项目文档</h4>
          <span class="muted">计划批准后由系统生成</span>
        </div>
        <el-table :data="documents" size="small" empty-text="暂无正式文档">
          <el-table-column prop="documentType" label="类型" width="130" />
          <el-table-column prop="title" label="文档" min-width="180" />
          <el-table-column prop="version" label="版本" width="80">
            <template #default="{ row }">v{{ row.version }}</template>
          </el-table-column>
          <el-table-column prop="status" label="状态" width="110" />
          <el-table-column label="操作" width="150">
            <template #default="{ row }">
              <el-button link type="primary" @click="openDocument(row)">查看</el-button>
              <el-button
                v-if="previousDocument(row)"
                link
                type="warning"
                @click="compareDocument(row)"
              >
                对比
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </template>
    </el-drawer>

    <el-dialog v-model="documentVisible" :title="documentDetail?.title || '项目文档'" width="80%">
      <pre class="document-pre">{{ documentDetail?.contentMarkdown }}</pre>
    </el-dialog>

    <el-dialog v-model="documentCompareVisible" :title="documentCompareTitle" width="82%">
      <div class="diff-list">
        <div
          v-for="(line, index) in documentCompareLines"
          :key="index"
          class="diff-line"
          :class="`diff-${line.type}`"
        >
          <span class="diff-sign">{{ line.type === 'add' ? '+' : line.type === 'remove' ? '-' : ' ' }}</span>
          <pre>{{ line.text }}</pre>
        </div>
      </div>
    </el-dialog>
  </div>
</template>

<style scoped>
.page-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 16px;
}

.page-head h2 {
  margin: 0 0 4px;
}

.actions {
  display: flex;
  gap: 8px;
}

.muted {
  color: #6b7280;
}

.detail-head {
  display: flex;
  justify-content: space-between;
  gap: 16px;
  align-items: flex-start;
  margin-bottom: 16px;
}

.detail-head h3 {
  margin: 0 0 4px;
}

.detail-pre {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: inherit;
  font-size: 13px;
}

.documents-head {
  display: flex;
  align-items: baseline;
  gap: 10px;
  margin: 20px 0 10px;
}

.documents-head h4 {
  margin: 0;
}

.document-pre {
  white-space: pre-wrap;
  word-break: break-word;
  font-size: 13px;
  line-height: 1.7;
  background: #f8fafc;
  border: 1px solid #e5e7eb;
  border-radius: 6px;
  padding: 14px;
  max-height: 70vh;
  overflow: auto;
}

.diff-list {
  max-height: 70vh;
  overflow: auto;
  border: 1px solid #e5e7eb;
  border-radius: 6px;
  font-size: 12px;
}

.diff-line {
  display: flex;
  gap: 8px;
  padding: 2px 8px;
  white-space: pre-wrap;
  word-break: break-word;
}

.diff-line pre {
  margin: 0;
  font-family: Consolas, monospace;
  white-space: pre-wrap;
}

.diff-sign {
  width: 14px;
  flex: none;
  color: #6b7280;
}

.diff-add {
  background: #ecfdf5;
}

.diff-remove {
  background: #fef2f2;
}

.diff-same {
  background: #ffffff;
}
</style>
