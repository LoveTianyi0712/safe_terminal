import { http } from './http'

export interface Terminal {
  id:           number
  terminalId:   string
  hostname:     string
  ipAddress:    string
  os:           number
  osVersion:    string
  agentVersion: string
  department:   string
  owner:        string
  policyId:     string
  updatedAt:    string
}

export interface Page<T> {
  content:       T[]
  totalElements: number
  totalPages:    number
  number:        number
  size:          number
}

export const terminalApi = {
  list: (params: { keyword?: string; page?: number; size?: number }) =>
    http.get<Page<Terminal>>('/v1/terminals', { params }),

  get: (terminalId: string) =>
    http.get<Terminal>(`/v1/terminals/${terminalId}`),

  onlineStatus: (terminalId: string) =>
    http.get<{ terminalId: string; online: boolean }>(`/v1/terminals/${terminalId}/online`),

  onlineCount: () =>
    http.get<{ count: number }>('/v1/terminals/stats/online-count'),

  update: (terminalId: string, data: { department?: string; owner?: string }) =>
    http.patch(`/v1/terminals/${terminalId}`, data),
}
