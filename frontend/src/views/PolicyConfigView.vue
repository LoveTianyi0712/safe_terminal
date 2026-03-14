<template>
  <div>
    <el-card>
      <template #header>
        <div class="card-header">
          <span>策略配置</span>
          <el-button type="primary" @click="openCreateDialog">新建策略</el-button>
        </div>
      </template>

      <el-table :data="policies" v-loading="loading">
        <el-table-column prop="name"                label="策略名称" />
        <el-table-column prop="logLevel"            label="日志级别" width="100">
          <template #default="{ row }">
            {{ ['INFO','WARNING','ERROR','CRITICAL'][row.logLevel] }}
          </template>
        </el-table-column>
        <el-table-column prop="heartbeatIntervalSec" label="心跳间隔(s)" width="120" />
        <el-table-column prop="usbAlertThreshold"   label="USB告警阈值" width="120" />
        <el-table-column prop="usbBlockEnabled"     label="USB阻断"    width="100">
          <template #default="{ row }">
            <el-tag :type="row.usbBlockEnabled ? 'danger' : 'success'" size="small">
              {{ row.usbBlockEnabled ? '启用' : '关闭' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="130" fixed="right">
          <template #default="{ row }">
            <el-button size="small" @click="openEdit(row)">编辑</el-button>
            <el-button size="small" type="danger" @click="deletePolicy(row.policyId)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 新建/编辑对话框 -->
    <el-dialog v-model="dialogVisible" :title="editMode ? '编辑策略' : '新建策略'" width="600px">
      <el-form :model="form" label-width="130px">
        <el-form-item label="策略名称" required>
          <el-input v-model="form.name" />
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="form.description" type="textarea" :rows="2" />
        </el-form-item>
        <el-form-item label="日志采集级别">
          <el-select v-model="form.logLevel">
            <el-option :value="0" label="INFO" />
            <el-option :value="1" label="WARNING" />
            <el-option :value="2" label="ERROR" />
            <el-option :value="3" label="CRITICAL" />
          </el-select>
        </el-form-item>
        <el-form-item label="心跳间隔(秒)">
          <el-input-number v-model="form.heartbeatIntervalSec" :min="10" :max="300" />
        </el-form-item>
        <el-form-item label="USB告警阈值/小时">
          <el-input-number v-model="form.usbAlertThreshold" :min="1" :max="100" />
        </el-form-item>
        <el-form-item label="USB阻断">
          <el-switch v-model="form.usbBlockEnabled" />
        </el-form-item>
        <el-form-item label="USB白名单">
          <el-select v-model="form.usbWhitelist" multiple filterable allow-create placeholder="输入设备ID前缀后回车" />
        </el-form-item>
        <el-form-item label="USB黑名单">
          <el-select v-model="form.usbBlacklist" multiple filterable allow-create placeholder="输入设备ID前缀后回车" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="submitForm">保存并下发</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { policyApi, type Policy } from '@/api/policy'

const policies      = ref<Policy[]>([])
const loading       = ref(false)
const dialogVisible = ref(false)
const editMode      = ref(false)
const editPolicyId  = ref('')

const defaultForm = (): Policy => ({
  name: '',
  description: '',
  logLevel: 1,
  heartbeatIntervalSec: 30,
  usbBlockEnabled: false,
  usbWhitelist: [],
  usbBlacklist: [],
  usbAlertThreshold: 3,
})
const form = reactive<Policy>(defaultForm())

async function loadPolicies() {
  loading.value = true
  try { policies.value = (await policyApi.list()) as Policy[] }
  finally { loading.value = false }
}

function openCreateDialog() {
  Object.assign(form, defaultForm())
  editMode.value = false
  dialogVisible.value = true
}

function openEdit(policy: Policy) {
  Object.assign(form, policy)
  editPolicyId.value = policy.policyId!
  editMode.value = true
  dialogVisible.value = true
}

async function submitForm() {
  try {
    if (editMode.value) {
      await policyApi.update(editPolicyId.value, form)
    } else {
      await policyApi.create(form)
    }
    ElMessage.success('策略已保存并下发到探针')
    dialogVisible.value = false
    loadPolicies()
  } catch { /* error handled by axios interceptor */ }
}

async function deletePolicy(id: string) {
  await ElMessageBox.confirm('确定删除该策略？', '警告', { type: 'warning' })
  await policyApi.delete(id)
  ElMessage.success('已删除')
  loadPolicies()
}

onMounted(loadPolicies)
</script>

<style scoped>
.card-header { display:flex; align-items:center; justify-content:space-between; }
</style>
