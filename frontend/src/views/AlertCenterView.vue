<template>
  <el-card>
    <template #header>
      <div class="card-header">
        <span>告警中心</span>
        <div class="filter-group">
          <el-select v-model="statusFilter" placeholder="状态筛选" clearable style="width:120px" @change="loadAlerts">
            <el-option label="未处理" :value="0" />
            <el-option label="已确认" :value="1" />
            <el-option label="已解决" :value="2" />
          </el-select>
          <el-badge :value="alertStore.openCount" :hidden="alertStore.openCount === 0" style="margin-left:12px">
            <el-tag type="danger">未处理: {{ alertStore.openCount }}</el-tag>
          </el-badge>
        </div>
      </div>
    </template>

    <!-- 实时滚动区域（最新 5 条动态展示） -->
    <div class="realtime-strip" v-if="realtimeAlerts.length">
      <el-alert
        v-for="a in realtimeAlerts"
        :key="a.id"
        :title="a.title"
        :description="`${a.hostname} · ${a.occurredAt}`"
        :type="a.severity >= 3 ? 'error' : 'warning'"
        show-icon
        closable
        style="margin-bottom:6px"
      />
    </div>

    <el-table :data="alertStore.alerts" v-loading="loading" stripe>
      <el-table-column prop="occurredAt" label="发生时间" width="180" />
      <el-table-column prop="hostname"   label="终端"     width="160" />
      <el-table-column prop="alertType"  label="类型"     width="130" />
      <el-table-column prop="title"      label="标题"     show-overflow-tooltip />
      <el-table-column label="等级"      width="90">
        <template #default="{ row }">
          <el-tag :type="severityTag(row.severity)" size="small">
            {{ severityLabel(row.severity) }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="状态" width="90">
        <template #default="{ row }">
          <el-tag :type="statusTag(row.status)" size="small">
            {{ statusLabel(row.status) }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="150" fixed="right">
        <template #default="{ row }">
          <el-button v-if="row.status === 0" size="small" @click="confirmAlert(row.id)">确认</el-button>
          <el-button v-if="row.status <= 1"  size="small" type="success" @click="resolveAlert(row.id)">解决</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-pagination
      v-model:current-page="currentPage"
      :total="total"
      layout="total, prev, pager, next"
      style="margin-top:12px;justify-content:flex-end"
      @current-change="loadAlerts"
    />
  </el-card>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useAlertStore } from '@/stores/alertStore'
import type { Alert } from '@/api/alert'

const alertStore    = useAlertStore()
const loading       = ref(false)
const statusFilter  = ref<number | undefined>(undefined)
const currentPage   = ref(1)
const total         = ref(0)
const realtimeAlerts = ref<Alert[]>([])

async function loadAlerts() {
  loading.value = true
  try {
    const page = await alertStore.loadAlerts({
      status: statusFilter.value,
      page: currentPage.value - 1,
    })
    total.value = (page as any).totalElements
  } finally {
    loading.value = false
  }
}

async function confirmAlert(id: number) {
  await alertStore.confirmAlert(id)
}
async function resolveAlert(id: number) {
  await alertStore.resolveAlert(id)
}

function severityTag(s: number)  { return (['info','warning','danger','danger'] as any)[s] }
function severityLabel(s: number){ return ['信息','警告','错误','严重'][s] ?? '未知' }
function statusTag(s: number)    { return (['danger','warning','success'] as any)[s] }
function statusLabel(s: number)  { return ['未处理','已确认','已解决'][s] ?? '未知' }

onMounted(loadAlerts)
</script>

<style scoped>
.card-header    { display:flex; align-items:center; justify-content:space-between; }
.filter-group   { display:flex; align-items:center; gap:8px; }
.realtime-strip { margin-bottom:12px; }
</style>
