import { http } from './http'
import type { Page } from './terminal'

export interface Alert {
  id:          number
  terminalId:  string
  hostname:    string
  alertType:   string
  severity:    number
  title:       string
  description: string
  status:      number    // 0=OPEN 1=CONFIRMED 2=RESOLVED
  operator:    string
  occurredAt:  string
  createdAt:   string
}

export const alertApi = {
  list: (params: { status?: number; terminalId?: string; page?: number; size?: number }) =>
    http.get<Page<Alert>>('/v1/alerts', { params }),

  stats: () =>
    http.get<{ open: number; today: number }>('/v1/alerts/stats'),

  confirm: (id: number) =>
    http.patch(`/v1/alerts/${id}/confirm`),

  resolve: (id: number, comment?: string) =>
    http.patch(`/v1/alerts/${id}/resolve`, { comment }),
}
