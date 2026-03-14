import { http } from './http'

export interface Policy {
  id?:                   number
  policyId?:             string
  name:                  string
  description?:          string
  logLevel:              number
  heartbeatIntervalSec:  number
  usbBlockEnabled:       boolean
  usbWhitelist:          string[]
  usbBlacklist:          string[]
  usbAlertThreshold:     number
}

export const policyApi = {
  list:   ()                              => http.get<Policy[]>('/v1/policies'),
  get:    (id: string)                    => http.get<Policy>(`/v1/policies/${id}`),
  create: (data: Policy)                  => http.post<Policy>('/v1/policies', data),
  update: (id: string, data: Policy)      => http.put<Policy>(`/v1/policies/${id}`, data),
  delete: (id: string)                    => http.delete(`/v1/policies/${id}`),
}
