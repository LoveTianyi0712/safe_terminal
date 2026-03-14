<template>
  <div>
    <el-card>
      <template #header>
        <div class="card-header">
          <span>终端管理</span>
          <el-input
            v-model="keyword"
            placeholder="搜索主机名/IP..."
            clearable
            style="width:240px"
            @change="loadData"
          >
            <template #prefix><el-icon><Search /></el-icon></template>
          </el-input>
        </div>
      </template>

      <el-table :data="terminals" v-loading="loading" stripe style="width:100%">
        <el-table-column prop="hostname"     label="主机名"   width="180" />
        <el-table-column prop="ipAddress"    label="IP地址"   width="140" />
        <el-table-column prop="os"           label="系统"     width="110">
          <template #default="{ row }">
            <el-tag size="small" :type="osTag(row.os)">{{ osLabel(row.os) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="osVersion"    label="系统版本" width="160" show-overflow-tooltip />
        <el-table-column prop="department"   label="部门"     width="120" />
        <el-table-column prop="owner"        label="责任人"   width="100" />
        <el-table-column label="在线状态"    width="100">
          <template #default="{ row }">
            <el-badge :is-dot="true" :type="onlineMap[row.terminalId] ? 'success' : 'info'" />
            {{ onlineMap[row.terminalId] ? '在线' : '离线' }}
          </template>
        </el-table-column>
        <el-table-column prop="agentVersion" label="探针版本" width="110" />
        <el-table-column prop="updatedAt"    label="最后活跃" width="180" />
        <el-table-column label="操作" fixed="right" width="100">
          <template #default="{ row }">
            <el-button size="small" @click="openDetail(row)">详情</el-button>
          </template>
        </el-table-column>
      </el-table>

      <el-pagination
        v-model:current-page="currentPage"
        v-model:page-size="pageSize"
        :total="total"
        layout="total, prev, pager, next"
        style="margin-top:12px;justify-content:flex-end"
        @current-change="loadData"
      />
    </el-card>

    <!-- 终端详情抽屉 -->
    <el-drawer v-model="drawerVisible" :title="`终端详情: ${selected?.hostname}`" size="50%">
      <TerminalDetailDrawer v-if="selected" :terminal-id="selected.terminalId" />
    </el-drawer>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { terminalApi, type Terminal } from '@/api/terminal'
import TerminalDetailDrawer from '@/components/TerminalDetailDrawer.vue'

const terminals    = ref<Terminal[]>([])
const loading      = ref(false)
const keyword      = ref('')
const currentPage  = ref(1)
const pageSize     = ref(20)
const total        = ref(0)
const onlineMap    = reactive<Record<string, boolean>>({})
const drawerVisible = ref(false)
const selected     = ref<Terminal | null>(null)

async function loadData() {
  loading.value = true
  try {
    const page = await terminalApi.list({
      keyword: keyword.value || undefined,
      page: currentPage.value - 1,
      size: pageSize.value,
    })
    terminals.value = (page as any).content ?? []
    total.value     = (page as any).totalElements ?? 0

    // 批量查询当前页所有终端的在线状态（一次请求）
    if (terminals.value.length > 0) {
      const ids    = terminals.value.map((t: Terminal) => t.terminalId)
      const status = await terminalApi.batchOnlineStatus(ids)
      Object.assign(onlineMap, status)
    }
  } finally {
    loading.value = false
  }
}

function openDetail(t: Terminal) {
  selected.value = t
  drawerVisible.value = true
}

function osLabel(os: number) { return ['未知','Windows','Linux','macOS'][os] ?? '未知' }
function osTag(os: number)   { return ['info','primary','success','warning'][os] ?? 'info' as any }

onMounted(loadData)
</script>

<style scoped>
.card-header { display: flex; align-items: center; justify-content: space-between; }
</style>
