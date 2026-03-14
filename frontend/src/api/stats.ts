import { http } from './http'

export interface DailyEventStat {
  date:       string
  logCount:   number
  usbCount:   number
  alertCount: number
}

export interface AlertTypeStat {
  name:  string
  value: number
}

export const statsApi = {
  eventTrend: (days = 7) =>
    http.get<DailyEventStat[]>('/v1/stats/event-trend', { params: { days } }),

  alertDistribution: () =>
    http.get<AlertTypeStat[]>('/v1/stats/alert-distribution'),
}
