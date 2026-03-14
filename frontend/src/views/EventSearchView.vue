<template>
  <el-card>
    <template #header>事件检索</template>

    <el-tabs v-model="activeTab" @tab-change="clearResults">
      <!-- ══ 日志事件 Tab ══════════════════════════════════════════════ -->
      <el-tab-pane label="📋 系统日志" name="logs">
        <el-form :model="logForm" inline class="search-form">
          <el-form-item label="终端ID">
            <el-input v-model="logForm.terminalId" placeholder="可选" clearable style="width:180px" />
          </el-form-item>
          <el-form-item label="关键词">
            <el-input v-model="logForm.keyword" placeholder="日志内容关键词" clearable style="width:220px" />
          </el-form-item>
          <el-form-item label="时间范围">
            <el-date-picker
              v-model="logForm.dateRange"
              type="daterange" range-separator="至"
              start-placeholder="开始日期" end-placeholder="结束日期"
              format="YYYY-MM-DD" value-format="YYYY-MM-DD"
              style="width:260px"
            />
          </el-form-item>
          <el-form-item>
            <el-button type="primary" :loading="logLoading" @click="searchLogs">
              <el-icon><Search /></el-icon> 搜索
            </el-button>
            <el-button @click="exportLogCsv">导出 CSV</el-button>
          </el-form-item>
        </el-form>

        <el-table :data="logResults" v-loading="logLoading" stripe max-height="520">
          <el-table-column prop="timestamp" label="时间"   width="190" />
          <el-table-column prop="hostname"  label="终端"   width="160" />
          <el-table-column prop="source"    label="来源"   width="130" />
          <el-table-column prop="eventId"   label="事件ID" width="110" />
          <el-table-column label="等级" width="90">
            <template #default="{ row }">
              <el-tag :type="severityTag(row.severity)" size="small">
                {{ ['INFO','WARN','ERROR','CRIT'][row.severity] ?? '?' }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="message" label="日志内容" show-overflow-tooltip />
        </el-table>

        <div class="result-footer" v-if="logTotal > 0">
          <span>共 {{ logTotal }} 条</span>
          <el-pagination
            v-model:current-page="logPage"
            v-model:page-size="logSize"
            :total="logTotal"
            layout="prev, pager, next, sizes"
            :page-sizes="[20, 50, 100]"
            @current-change="searchLogs"
            @size-change="searchLogs"
          />
        </div>
      </el-tab-pane>

      <!-- ══ USB 事件 Tab ════════════════════════════════════════════ -->
      <el-tab-pane label="🔌 USB 事件" name="usb">
        <el-form :model="usbForm" inline class="search-form">
          <el-form-item label="终端ID">
            <el-input v-model="usbForm.terminalId" placeholder="可选" clearable style="width:180px" />
          </el-form-item>
          <el-form-item label="设备ID">
            <el-input v-model="usbForm.deviceId" placeholder="如 VID_0951&PID_1666" clearable style="width:200px" />
          </el-form-item>
          <el-form-item label="动作">
            <el-select v-model="usbForm.action" placeholder="全部" clearable style="width:110px">
              <el-option label="接入" :value="0" />
              <el-option label="移除" :value="1" />
            </el-select>
          </el-form-item>
          <el-form-item label="时间范围">
            <el-date-picker
              v-model="usbForm.dateRange"
              type="daterange" range-separator="至"
              start-placeholder="开始日期" end-placeholder="结束日期"
              format="YYYY-MM-DD" value-format="YYYY-MM-DD"
              style="width:260px"
            />
          </el-form-item>
          <el-form-item>
            <el-button type="primary" :loading="usbLoading" @click="searchUsb">
              <el-icon><Search /></el-icon> 搜索
            </el-button>
            <el-button @click="exportUsbCsv">导出 CSV</el-button>
          </el-form-item>
        </el-form>

        <el-table :data="usbResults" v-loading="usbLoading" stripe max-height="520">
          <el-table-column prop="timestamp"    label="时间"   width="190" />
          <el-table-column prop="hostname"     label="终端"   width="160" />
          <el-table-column label="动作" width="90">
            <template #default="{ row }">
              <el-tag :type="row.action === 0 ? 'success' : 'info'" size="small">
                {{ row.action === 0 ? '接入' : '移除' }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="deviceId"     label="设备ID"   width="200" show-overflow-tooltip />
          <el-table-column prop="vendor"       label="厂商"     width="140" show-overflow-tooltip />
          <el-table-column prop="product"      label="产品名"   width="160" show-overflow-tooltip />
          <el-table-column prop="serialNumber" label="序列号"   width="160" show-overflow-tooltip />
        </el-table>

        <div class="result-footer" v-if="usbTotal > 0">
          <span>共 {{ usbTotal }} 条</span>
          <el-pagination
            v-model:current-page="usbPage"
            v-model:page-size="usbSize"
            :total="usbTotal"
            layout="prev, pager, next, sizes"
            :page-sizes="[20, 50, 100]"
            @current-change="searchUsb"
            @size-change="searchUsb"
          />
        </div>
      </el-tab-pane>
    </el-tabs>
  </el-card>
</template>

<script setup lang="ts">
import { ref, reactive } from 'vue'
import { http } from '@/api/http'

const activeTab = ref<'logs' | 'usb'>('logs')

// ── 日志事件 ───────────────────────────────────────────────────────────
const logLoading = ref(false)
const logResults = ref<any[]>([])
const logTotal   = ref(0)
const logPage    = ref(1)
const logSize    = ref(20)

const logForm = reactive({
  terminalId: '',
  keyword:    '',
  dateRange:  [] as string[],
})

async function searchLogs() {
  logLoading.value = true
  try {
    const params: Record<string, any> = {
      page: logPage.value - 1,
      size: logSize.value,
    }
    if (logForm.terminalId)          params.terminalId = logForm.terminalId
    if (logForm.keyword)             params.keyword    = logForm.keyword
    if (logForm.dateRange?.length === 2) {
      params.startDate = logForm.dateRange[0]
      params.endDate   = logForm.dateRange[1]
    }
    const res = await http.get('/v1/events/logs', { params }) as any
    logResults.value = res.content ?? []
    logTotal.value   = res.totalElements ?? 0
  } finally {
    logLoading.value = false
  }
}

function exportLogCsv() {
  downloadCsv(
    'timestamp,hostname,source,eventId,severity,message',
    logResults.value.map(r =>
      [r.timestamp, r.hostname, r.source, r.eventId, r.severity, `"${r.message}"`].join(',')),
    'log_events',
  )
}

// ── USB 事件 ───────────────────────────────────────────────────────────
const usbLoading = ref(false)
const usbResults = ref<any[]>([])
const usbTotal   = ref(0)
const usbPage    = ref(1)
const usbSize    = ref(20)

const usbForm = reactive({
  terminalId: '',
  deviceId:   '',
  action:     null as number | null,
  dateRange:  [] as string[],
})

async function searchUsb() {
  usbLoading.value = true
  try {
    const params: Record<string, any> = {
      page: usbPage.value - 1,
      size: usbSize.value,
    }
    if (usbForm.terminalId)           params.terminalId = usbForm.terminalId
    if (usbForm.deviceId)             params.deviceId   = usbForm.deviceId
    if (usbForm.dateRange?.length === 2) {
      params.startDate = usbForm.dateRange[0]
      params.endDate   = usbForm.dateRange[1]
    }
    const res = await http.get('/v1/events/usb', { params }) as any
    // action 过滤在前端完成（后端暂不支持 action 精确过滤）
    const raw = (res.content ?? []) as any[]
    usbResults.value = usbForm.action != null
      ? raw.filter((r: any) => r.action === usbForm.action)
      : raw
    usbTotal.value   = res.totalElements ?? 0
  } finally {
    usbLoading.value = false
  }
}

function exportUsbCsv() {
  downloadCsv(
    'timestamp,hostname,action,deviceId,vendor,product,serialNumber',
    usbResults.value.map(r =>
      [r.timestamp, r.hostname, r.action === 0 ? '接入' : '移除',
       r.deviceId, r.vendor, r.product, r.serialNumber].join(',')),
    'usb_events',
  )
}

// ── 通用 ───────────────────────────────────────────────────────────────
function clearResults() {
  logResults.value = []; logTotal.value = 0; logPage.value = 1
  usbResults.value = []; usbTotal.value = 0; usbPage.value = 1
}

function downloadCsv(header: string, rows: string[], name: string) {
  const blob = new Blob([header + '\n' + rows.join('\n')], { type: 'text/csv;charset=utf-8;' })
  const url  = URL.createObjectURL(blob)
  const a    = document.createElement('a')
  a.href     = url
  a.download = `${name}_${Date.now()}.csv`
  a.click()
  URL.revokeObjectURL(url)
}

function severityTag(s: number) {
  return (['info','warning','danger','danger'] as const)[s] ?? 'info'
}
</script>

<style scoped>
.search-form   { flex-wrap: wrap; margin-bottom: 12px; }
.result-footer { display: flex; align-items: center; justify-content: space-between; margin-top: 12px; color: #909399; font-size: 13px; }
</style>
