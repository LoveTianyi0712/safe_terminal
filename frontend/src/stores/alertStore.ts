import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { Alert } from '@/api/alert'
import { alertApi } from '@/api/alert'
import { ElNotification } from 'element-plus'

export const useAlertStore = defineStore('alert', () => {
  const alerts    = ref<Alert[]>([])
  const openCount = ref(0)
  const todayCount = ref(0)

  async function fetchStats() {
    const stats = await alertApi.stats()
    openCount.value  = stats.open
    todayCount.value = stats.today
  }

  function addRealtimeAlert(alert: Alert) {
    alerts.value.unshift(alert)
    openCount.value++

    // 桌面通知
    ElNotification({
      title:   `告警: ${alert.title}`,
      message: alert.description,
      type:    alert.severity >= 3 ? 'error' : 'warning',
      duration: 6000,
    })
  }

  async function loadAlerts(params?: { status?: number; page?: number }) {
    const page = await alertApi.list({ ...params, size: 20 })
    alerts.value = page.content
    return page
  }

  async function confirmAlert(id: number) {
    await alertApi.confirm(id)
    const a = alerts.value.find(a => a.id === id)
    if (a) a.status = 1
  }

  async function resolveAlert(id: number, comment?: string) {
    await alertApi.resolve(id, comment)
    const a = alerts.value.find(a => a.id === id)
    if (a) { a.status = 2; openCount.value = Math.max(0, openCount.value - 1) }
  }

  return { alerts, openCount, todayCount, fetchStats, addRealtimeAlert, loadAlerts, confirmAlert, resolveAlert }
})
