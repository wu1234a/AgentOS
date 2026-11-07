<script setup>
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { createApproval, decideApproval, listApprovals } from '../api/approvals'
import { formatUtc } from '../utils/format'

const items = ref([])
const total = ref(0)
const loading = ref(false)
const error = ref('')
const createVisible = ref(false)
const decision = reactive({ visible: false, id: '', action: 'Approved', reason: '', saving: false })
const form = reactive({ targetType: 'CodingRun', targetId: '', requestReason: '' })

async function load() {
  loading.value = true
  error.value = ''
  try {
    const data = await listApprovals()
    items.value = data.items
    total.value = data.total
  } catch (exception) {
    error.value = exception.message
  } finally {
    loading.value = false
  }
}

async function save() {
  try {
    await createApproval(form)
    ElMessage.success('审批申请已创建')
    createVisible.value = false
    Object.assign(form, { targetType: 'CodingRun', targetId: '', requestReason: '' })
    await load()
  } catch (exception) {
    ElMessage.error(exception.message)
  }
}

function openDecision(row, action) {
  Object.assign(decision, { visible: true, id: row.id, action, reason: '', saving: false })
}

async function submitDecision() {
  decision.saving = true
  try {
    await decideApproval(decision.id, { decision: decision.action, reason: decision.reason })
    ElMessage.success(decision.action === 'Approved' ? '已批准' : '已拒绝')
    decision.visible = false
    await load()
  } catch (exception) {
    ElMessage.error(exception.message)
  } finally {
    decision.saving = false
  }
}

onMounted(load)
</script>

<template>
  <div>
    <div class="page-head">
      <div>
        <h2>Approval Center</h2>
        <p class="muted">审批只记录决策，不执行、不合并、不改变 Coding Run 状态</p>
      </div>
      <el-button type="primary" @click="createVisible = true">发起审批</el-button>
    </div>
    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />
    <el-table v-loading="loading" :data="items" empty-text="暂无审批">
      <el-table-column prop="targetType" label="类型" width="130" />
      <el-table-column prop="targetId" label="目标" min-width="240" />
      <el-table-column prop="status" label="状态" width="110" />
      <el-table-column prop="requestedByUserId" label="申请人" width="130" />
      <el-table-column label="申请时间" width="180">
        <template #default="{ row }">{{ formatUtc(row.requestedAtUtc) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="180">
        <template #default="{ row }">
          <template v-if="row.status === 'Pending'">
            <el-button link type="success" @click="openDecision(row, 'Approved')">批准</el-button>
            <el-button link type="danger" @click="openDecision(row, 'Rejected')">拒绝</el-button>
          </template>
          <span v-else>{{ row.decisionReason || '-' }}</span>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="createVisible" title="发起审批" width="560px">
      <el-form label-width="100px">
        <el-form-item label="目标类型">
          <el-select v-model="form.targetType">
            <el-option label="CodingRun" value="CodingRun" />
            <el-option label="PlannerDraft" value="PlannerDraft" />
          </el-select>
        </el-form-item>
        <el-form-item label="目标 ID">
          <el-input v-model="form.targetId" placeholder="目标 GUID" />
        </el-form-item>
        <el-form-item label="申请理由">
          <el-input v-model="form.requestReason" type="textarea" :rows="3" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false">取消</el-button>
        <el-button type="primary" @click="save">提交</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="decision.visible" :title="decision.action === 'Approved' ? '批准' : '拒绝'" width="480px">
      <el-form label-width="80px">
        <el-form-item label="理由">
          <el-input v-model="decision.reason" type="textarea" :rows="3" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="decision.visible = false">取消</el-button>
        <el-button type="primary" :loading="decision.saving" @click="submitDecision">提交</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.page-head { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; }
.muted { color: #6b7280; margin: 4px 0 0; }
</style>
