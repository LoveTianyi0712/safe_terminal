<template>
  <el-card>
    <template #header>事件检索</template>

    <!-- 搜索表单 -->
    <el-form :model="searchForm" inline class="search-form">
      <el-form-item label="终端ID">
        <el-input v-model="searchForm.terminalId" placeholder="可选" clearable style="width:180px" />
      </el-form-item>
      <el-form-item label="关键词">
        <el-input v-model="searchForm.keyword" placeholder="日志内容关键词" clearable style="width:220px" />
      </el-form-item>
      <el-form-item label="时间范围">
        <el-date-picker
          v-model="searchForm.dateRange"
          type="daterange"
          range-separator="至"
          start-placeholder="开始日期"
          end-placeholder="结束日期"
          format="YYYY-MM-DD"
          value-format="YYYY-MM-DD"
          style="width:260px"
        />
      </el-form-item>
      <el-form-item>
        <el-button type="primary" :loading="loading" @click="doSearch">
          <el-icon><Search /></el-icon> 搜索
        </el-button>
        <el-button @click="exportCsv">导出 CSV</el-button>
      </el-form-item>
    </el-form>

    <el-table :data="results" v-loading="loading" stripe max-height="540">
      <el-table-column prop="timestamp" label="时间"   width="190" />
      <el-table-column prop="hostname"  label="终端"   width="160" />
      <el-table-column prop="source"    label="来源"   width="130" />
      <el-table-column prop="eventId"   label="事件ID" width="110" />
      <el-table-column label="等级"     width="90">
        <template #default="{ row }">
          <el-tag :type="severityTag(row.severity)" size="small">
            {{ ['INFO','WARN','ERROR','CRIT'][row.severity] ?? '?' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="message" label="日志内容" show-overflow-tooltip />
    </el-table>

    <div class="result-stats" v-if="results.length">
      共 {{ results.length }} 条结果
    </div>
  </el-card>
</template>

<script setup lang="ts">
import { ref, reactive } from 'vue'
import { http } from '@/api/http'

const loading = ref(false)
const results = ref<any[]>([])

const searchForm = reactive({
  terminalId: '',
  keyword:    '',
  dateRange:  [] as string[],
})

async function doSearch() {
  loading.value = true
  try {
    const params: Record<string, string> = {}
    if (searchForm.terminalId) params.terminalId = searchForm.terminalId
    if (searchForm.keyword)    params.keyword    = searchForm.keyword
    if (searchForm.dateRange?.length === 2) {
      params.startDate = searchForm.dateRange[0]
      params.endDate   = searchForm.dateRange[1]
    }
    results.value = (await http.get('/v1/events/logs', { params })) as any[]
  } finally {
    loading.value = false
  }
}

function exportCsv() {
  const header  = 'timestamp,hostname,source,eventId,severity,message\n'
  const rows    = results.value.map(r =>
    [r.timestamp, r.hostname, r.source, r.eventId, r.severity, `"${r.message}"`].join(',')
  ).join('\n')
  const blob    = new Blob([header + rows], { type: 'text/csv;charset=utf-8;' })
  const url     = URL.createObjectURL(blob)
  const a       = document.createElement('a')
  a.href        = url
  a.download    = `events_export_${Date.now()}.csv`
  a.click()
  URL.revokeObjectURL(url)
}

function severityTag(s: number) {
  return (['info','warning','danger','danger'] as any)[s] ?? 'info'
}
</script>

<style scoped>
.search-form  { flex-wrap: wrap; }
.result-stats { margin-top: 10px; color: #909399; font-size: 13px; }
</style>
