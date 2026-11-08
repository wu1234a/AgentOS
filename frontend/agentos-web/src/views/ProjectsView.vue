<script setup>
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { createProject, listProjects, setProjectEnabled, updateProject } from '../api/projects'

const items = ref([])
const loading = ref(false)
const error = ref('')
const createVisible = ref(false)
const form = reactive({ name: '', slug: '', maxConcurrentTasks: 2 })
const edit = reactive({ visible: false, id: '', name: '', maxConcurrentTasks: 2 })

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

async function save() {
  try {
    await createProject({ ...form, isEnabled: true })
    ElMessage.success('项目已创建')
    createVisible.value = false
    Object.assign(form, { name: '', slug: '', maxConcurrentTasks: 2 })
    await load()
  } catch (exception) {
    ElMessage.error(exception.message)
  }
}

function openEdit(row) {
  Object.assign(edit, { visible: true, id: row.id, name: row.name, maxConcurrentTasks: row.maxConcurrentTasks || 1 })
}

async function saveEdit() {
  try {
    await updateProject(edit.id, { name: edit.name, maxConcurrentTasks: edit.maxConcurrentTasks })
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
  <div>
    <div class="page-head">
      <div>
        <h2>Projects</h2>
        <p class="muted">项目注册、配额与启停；配额限制活动任务领取</p>
      </div>
      <el-button type="primary" @click="createVisible = true">注册项目</el-button>
    </div>
    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />
    <el-table v-loading="loading" :data="items" empty-text="暂无项目">
      <el-table-column prop="name" label="名称" min-width="180" />
      <el-table-column prop="slug" label="Slug" min-width="140" />
      <el-table-column label="状态" width="100">
        <template #default="{ row }">
          <el-tag :type="row.isEnabled ? 'success' : 'info'">{{ row.isEnabled ? '启用' : '停用' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="并发配额" width="120">
        <template #default="{ row }">{{ row.maxConcurrentTasks ?? '不限' }}</template>
      </el-table-column>
      <el-table-column label="操作" width="220">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          <el-button link :type="row.isEnabled ? 'warning' : 'success'" @click="toggle(row)">
            {{ row.isEnabled ? '停用' : '启用' }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="createVisible" title="注册项目" width="420px">
      <el-form label-width="100px">
        <el-form-item label="名称"><el-input v-model="form.name" /></el-form-item>
        <el-form-item label="Slug"><el-input v-model="form.slug" placeholder="lowercase-slug" /></el-form-item>
        <el-form-item label="并发配额"><el-input-number v-model="form.maxConcurrentTasks" :min="1" :max="10000" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false">取消</el-button>
        <el-button type="primary" @click="save">创建</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="edit.visible" title="编辑项目" width="420px">
      <el-form label-width="100px">
        <el-form-item label="名称"><el-input v-model="edit.name" /></el-form-item>
        <el-form-item label="并发配额"><el-input-number v-model="edit.maxConcurrentTasks" :min="1" :max="10000" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="edit.visible = false">取消</el-button>
        <el-button type="primary" @click="saveEdit">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>
