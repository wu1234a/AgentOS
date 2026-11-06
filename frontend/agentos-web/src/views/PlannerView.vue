<script setup>
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { approveDraft, createDraft, listDrafts, rejectDraft } from '../api/planner'
import { listModelProfiles } from '../api/modelProfiles'
import { formatUtc, statusTagType } from '../utils/format'

const items = ref([])
const total = ref(0)
const loading = ref(false)
const error = ref('')
const statusFilter = ref('')
const page = ref(1)
const pageSize = 50
const profiles = ref([])
const dialogVisible = ref(false)
const contentVisible = ref(false)
const content = ref('')
const contentTitle = ref('')
const form = reactive({ title: '', goal: '', context: '', modelProfileId: '' })

async function load() {
  loading.value = true
  error.value = ''
  try {
    const data = await listDrafts({
      status: statusFilter.value || undefined,
      limit: pageSize,
      offset: (page.value - 1) * pageSize
    })
    items.value = data.items
    total.value = data.total
  } catch (exception) {
    error.value = exception.message
  } finally {
    loading.value = false
  }
}

async function loadProfiles() {
  try {
    const data = await listModelProfiles({ enabled: true })
    profiles.value = data.items.filter((profile) => profile.executionMode === 'api')
  } catch {
    profiles.value = []
  }
}

function openCreate() {
  Object.assign(form, { title: '', goal: '', context: '', modelProfileId: '' })
  dialogVisible.value = true
}

async function save() {
  try {
    await createDraft({
      title: form.title,
      goal: form.goal,
      context: form.context || null,
      modelProfileId: form.modelProfileId || null
    })
    ElMessage.success('草案已生成，等待人工审阅')
    dialogVisible.value = false
    await load()
  } catch (exception) {
    ElMessage.error(exception.message)
  }
}

function view(row) {
  content.value = row.contentMarkdown
  contentTitle.value = row.title
  contentVisible.value = true
}

async function decide(row, action) {
  try {
    if (action === 'approve') {
      await approveDraft(row.id)
      ElMessage.success('已批准')
    } else {
      await rejectDraft(row.id)
      ElMessage.success('已拒绝')
    }
    await load()
  } catch (exception) {
    ElMessage.error(exception.message)
  }
}

onMounted(async () => {
  await load()
  await loadProfiles()
})
</script>

<template>
  <div>
    <div class="page-head">
      <div>
        <h2>Planner</h2>
        <p class="muted">由 Kimi/API 模型生成任务草案，必须人工批准后才进入实现</p>
      </div>
      <div class="actions">
        <el-select
          v-model="statusFilter"
          placeholder="全部状态"
          style="width: 150px"
          @change="() => { page = 1; load() }"
        >
          <el-option label="全部状态" value="" />
          <el-option label="PendingReview" value="PendingReview" />
          <el-option label="Approved" value="Approved" />
          <el-option label="Rejected" value="Rejected" />
        </el-select>
        <el-button type="primary" @click="openCreate">生成草案</el-button>
      </div>
    </div>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />

    <el-table v-loading="loading" :data="items" empty-text="暂无草案">
      <el-table-column prop="title" label="标题" min-width="220" />
      <el-table-column prop="modelProfileCode" label="模型" width="160" />
      <el-table-column label="状态" width="140">
        <template #default="{ row }">
          <el-tag :type="statusTagType(row.status === 'Approved' ? 'Succeeded' : row.status === 'Rejected' ? 'Failed' : 'Queued')" size="small">
            {{ row.status }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="创建时间" width="180">
        <template #default="{ row }">{{ formatUtc(row.createdAtUtc) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="240">
        <template #default="{ row }">
          <el-button link type="primary" @click="view(row)">查看</el-button>
          <el-button
            v-if="row.status === 'PendingReview'"
            link
            type="success"
            @click="decide(row, 'approve')"
          >
            批准
          </el-button>
          <el-button
            v-if="row.status === 'PendingReview'"
            link
            type="danger"
            @click="decide(row, 'reject')"
          >
            拒绝
          </el-button>
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

    <el-dialog v-model="dialogVisible" title="生成任务草案" width="720px">
      <el-form label-width="110px">
        <el-form-item label="标题">
          <el-input v-model="form.title" maxlength="200" />
        </el-form-item>
        <el-form-item label="目标">
          <el-input v-model="form.goal" type="textarea" :rows="4" maxlength="4000" />
        </el-form-item>
        <el-form-item label="上下文">
          <el-input v-model="form.context" type="textarea" :rows="4" maxlength="8000" />
        </el-form-item>
        <el-form-item label="模型 Profile">
          <el-select v-model="form.modelProfileId" clearable placeholder="使用默认 Planner 模型">
            <el-option
              v-for="profile in profiles"
              :key="profile.id"
              :label="`${profile.code} (${profile.provider}/${profile.model})`"
              :value="profile.id"
            />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="save">生成</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="contentVisible" :title="contentTitle" width="760px">
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
.pager {
  margin-top: 16px;
  justify-content: flex-end;
}

.markdown {
  white-space: pre-wrap;
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 12px;
  background: #0f172a;
  color: #e2e8f0;
  padding: 12px;
  border-radius: 6px;
  max-height: 520px;
  overflow: auto;
}
</style>
