<template>
  <div v-loading="loading">
    <el-descriptions :column="2" border size="small">
      <el-descriptions-item label="IP地址">{{ terminal?.ipAddress }}</el-descriptions-item>
      <el-descriptions-item label="系统">{{ osLabel(terminal?.os) }} {{ terminal?.osVersion }}</el-descriptions-item>
      <el-descriptions-item label="部门">{{ terminal?.department || '未设置' }}</el-descriptions-item>
      <el-descriptions-item label="责任人">{{ terminal?.owner || '未设置' }}</el-descriptions-item>
    </el-descriptions>

    <el-divider>最近事件（最新20条）</el-divider>
    <el-timeline>
      <el-timeline-item
        v-for="e in recentEvents"
        :key="e.id"
        :timestamp="e.timestamp"
        :type="timelineType(e.severity)"
      >
        <b>{{ e.source }}</b> {{ e.message }}
      </el-timeline-item>
    </el-timeline>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { terminalApi, type Terminal } from '@/api/terminal'
import { http } from '@/api/http'

const props = defineProps<{ terminalId: string }>()

const loading      = ref(false)
const terminal     = ref<Terminal | null>(null)
const recentEvents = ref<any[]>([])

function osLabel(os?: number) { return ['未知','Windows','Linux','macOS'][os ?? 0] }
function timelineType(s: number) {
  return (['primary','warning','danger','danger'] as any)[s] ?? 'primary'
}

onMounted(async () => {
  loading.value = true
  try {
    terminal.value = (await terminalApi.get(props.terminalId)) as Terminal
    recentEvents.value = (await http.get('/v1/events/logs', {
      params: { terminalId: props.terminalId, size: 20 },
    })) as any[]
  } finally {
    loading.value = false
  }
})
</script>
