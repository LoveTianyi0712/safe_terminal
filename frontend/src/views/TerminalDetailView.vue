<template>
  <div v-loading="loading">
    <el-descriptions :title="`终端详情：${terminal?.hostname}`" :column="2" border>
      <el-descriptions-item label="终端ID">{{ terminal?.terminalId }}</el-descriptions-item>
      <el-descriptions-item label="IP地址">{{ terminal?.ipAddress }}</el-descriptions-item>
      <el-descriptions-item label="操作系统">{{ osLabel(terminal?.os) }}</el-descriptions-item>
      <el-descriptions-item label="系统版本">{{ terminal?.osVersion }}</el-descriptions-item>
      <el-descriptions-item label="探针版本">{{ terminal?.agentVersion }}</el-descriptions-item>
      <el-descriptions-item label="在线状态">
        <el-tag :type="online ? 'success' : 'info'">{{ online ? '在线' : '离线' }}</el-tag>
      </el-descriptions-item>
      <el-descriptions-item label="部门">{{ terminal?.department }}</el-descriptions-item>
      <el-descriptions-item label="责任人">{{ terminal?.owner }}</el-descriptions-item>
    </el-descriptions>

    <el-tabs style="margin-top:20px">
      <el-tab-pane label="最近事件">
        <el-table :data="recentEvents" stripe max-height="400">
          <el-table-column prop="timestamp" label="时间"    width="190" />
          <el-table-column prop="source"    label="来源"    width="130" />
          <el-table-column prop="message"   label="内容"    show-overflow-tooltip />
        </el-table>
      </el-tab-pane>
      <el-tab-pane label="USB记录">
        <el-table :data="usbEvents" stripe max-height="400">
          <el-table-column prop="timestamp"    label="时间"     width="190" />
          <el-table-column prop="action"       label="动作"     width="90">
            <template #default="{ row }">
              <el-tag :type="row.action === 0 ? 'success' : 'info'" size="small">
                {{ row.action === 0 ? '接入' : '移除' }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="vendor"    label="厂商"     width="120" />
          <el-table-column prop="product"   label="产品"     width="150" />
          <el-table-column prop="deviceId"  label="设备ID"   show-overflow-tooltip />
        </el-table>
      </el-tab-pane>
      <el-tab-pane label="告警历史">
        <el-table :data="terminalAlerts" stripe max-height="400">
          <el-table-column prop="occurredAt" label="时间"   width="190" />
          <el-table-column prop="title"      label="告警"   />
          <el-table-column prop="status"     label="状态"   width="90">
            <template #default="{ row }">
              <el-tag :type="(['danger','warning','success'] as any)[row.status]" size="small">
                {{ ['未处理','已确认','已解决'][row.status] }}
              </el-tag>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { terminalApi, type Terminal } from '@/api/terminal'
import { alertApi } from '@/api/alert'
import { http } from '@/api/http'

const route          = useRoute()
const loading        = ref(false)
const terminal       = ref<Terminal | null>(null)
const online         = ref(false)
const recentEvents   = ref<any[]>([])
const usbEvents      = ref<any[]>([])
const terminalAlerts = ref<any[]>([])

const terminalId = route.params.id as string

function osLabel(os?: number) { return ['未知','Windows','Linux','macOS'][os ?? 0] }

onMounted(async () => {
  loading.value = true
  try {
    const [t, o, alerts, logs, usb] = await Promise.all([
      terminalApi.get(terminalId),
      terminalApi.onlineStatus(terminalId),
      alertApi.list({ terminalId, size: 10 }),
      http.get('/v1/events/logs', { params: { terminalId, size: 20 } }),
      http.get('/v1/events/usb',  { params: { terminalId, size: 20 } }),
    ])
    terminal.value       = t as Terminal
    online.value         = (o as any).online
    terminalAlerts.value = (alerts as any).content ?? []
    recentEvents.value   = (logs as any).content   ?? []
    usbEvents.value      = (usb  as any).content   ?? []
  } finally {
    loading.value = false
  }
})
</script>
