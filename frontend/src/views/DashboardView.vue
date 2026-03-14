<template>
  <div class="dashboard">
    <!-- 统计卡片 -->
    <el-row :gutter="16" class="stat-cards">
      <el-col :span="6" v-for="card in statCards" :key="card.label">
        <el-card shadow="hover" :body-style="{ padding: '20px' }">
          <div class="stat-card">
            <el-icon :size="36" :color="card.color"><component :is="card.icon" /></el-icon>
            <div class="stat-info">
              <div class="stat-value">{{ card.value }}</div>
              <div class="stat-label">{{ card.label }}</div>
            </div>
          </div>
        </el-card>
      </el-col>
    </el-row>

    <!-- 图表区域 -->
    <el-row :gutter="16" class="charts">
      <el-col :span="14">
        <el-card header="近7日事件趋势" shadow="hover">
          <v-chart :option="eventTrendOption" style="height:300px" autoresize />
        </el-card>
      </el-col>
      <el-col :span="10">
        <el-card header="告警类型分布" shadow="hover">
          <v-chart :option="alertDistOption" style="height:300px" autoresize />
        </el-card>
      </el-col>
    </el-row>

    <!-- 最新告警 -->
    <el-card header="最新告警" style="margin-top:16px">
      <el-table :data="alertStore.alerts.slice(0, 10)" stripe>
        <el-table-column prop="occurredAt" label="时间"   width="180" />
        <el-table-column prop="hostname"   label="终端"   width="160" />
        <el-table-column prop="title"      label="告警标题" />
        <el-table-column prop="severity"   label="等级"   width="90">
          <template #default="{ row }">
            <el-tag :type="severityTag(row.severity)" size="small">
              {{ severityLabel(row.severity) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="status" label="状态" width="90">
          <template #default="{ row }">
            <el-tag :type="statusTag(row.status)" size="small">
              {{ statusLabel(row.status) }}
            </el-tag>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { Monitor, Warning, CircleCheck, DataLine } from '@element-plus/icons-vue'
import VChart from 'vue-echarts'
import { use } from 'echarts/core'
import { LineChart, PieChart } from 'echarts/charts'
import { GridComponent, TooltipComponent, LegendComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import { useAlertStore } from '@/stores/alertStore'
import { terminalApi } from '@/api/terminal'

use([LineChart, PieChart, GridComponent, TooltipComponent, LegendComponent, CanvasRenderer])

const alertStore  = useAlertStore()
const totalTerminals = ref(0)
const onlineCount    = ref(0)

const statCards = computed(() => [
  { label: '总终端数', value: totalTerminals.value, icon: 'Monitor',      color: '#409EFF' },
  { label: '在线终端', value: onlineCount.value,    icon: 'CircleCheck',  color: '#67C23A' },
  { label: '今日告警', value: alertStore.todayCount, icon: 'Warning',     color: '#E6A23C' },
  { label: '未处理告警', value: alertStore.openCount, icon: 'DataLine',   color: '#F56C6C' },
])

// 近7日事件趋势（模拟数据，实际应调用 API）
const eventTrendOption = ref({
  tooltip: { trigger: 'axis' },
  legend: { data: ['日志事件', 'USB事件', '告警'] },
  xAxis: { type: 'category', data: ['3/8','3/9','3/10','3/11','3/12','3/13','3/14'] },
  yAxis: { type: 'value' },
  series: [
    { name: '日志事件', type: 'line', smooth: true, data: [1200,980,1100,1400,1050,1300,1180] },
    { name: 'USB事件', type: 'line', smooth: true, data: [45,32,58,71,43,62,55] },
    { name: '告警',    type: 'line', smooth: true, data: [8,5,12,15,6,10,9] },
  ],
})

const alertDistOption = ref({
  tooltip: { trigger: 'item' },
  legend: { bottom: 0 },
  series: [{
    name: '告警类型',
    type: 'pie',
    radius: ['40%', '70%'],
    data: [
      { value: 48, name: 'USB滥用' },
      { value: 23, name: '黑名单设备' },
      { value: 15, name: '异常登录' },
      { value: 14, name: '其他' },
    ],
  }],
})

function severityTag(s: number) { return ['info','warning','danger','danger'][s] ?? 'info' }
function severityLabel(s: number) { return ['信息','警告','错误','严重'][s] ?? '未知' }
function statusTag(s: number)   { return ['danger','warning','success'][s] ?? 'info' }
function statusLabel(s: number) { return ['未处理','已确认','已解决'][s] ?? '未知' }

onMounted(async () => {
  const [onlineCnt, pageData] = await Promise.all([
    terminalApi.onlineCount(),
    terminalApi.list({ size: 1 }),
  ])
  onlineCount.value    = onlineCnt.count
  totalTerminals.value = (pageData as any).totalElements ?? 0
  alertStore.fetchStats()
  alertStore.loadAlerts({ status: 0 })
})
</script>

<style scoped>
.dashboard { padding: 4px; }
.stat-cards { margin-bottom: 16px; }
.stat-card  { display: flex; align-items: center; gap: 16px; }
.stat-value { font-size: 28px; font-weight: 700; }
.stat-label { color: #909399; font-size: 13px; margin-top: 4px; }
.charts     { margin-bottom: 0; }
</style>
