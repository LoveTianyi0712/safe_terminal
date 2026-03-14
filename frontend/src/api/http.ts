import axios from 'axios'
import { ElMessage } from 'element-plus'
import router from '@/router'

export const http = axios.create({
  baseURL: '/api',
  timeout: 15_000,
})

export function setupAxios() {
  http.interceptors.request.use((config) => {
    const token = localStorage.getItem('token')
    if (token) {
      config.headers.Authorization = `Bearer ${token}`
    }
    return config
  })

  http.interceptors.response.use(
    (res) => res.data,
    (err) => {
      const status = err.response?.status
      if (status === 401) {
        localStorage.removeItem('token')
        router.push('/login')
        return
      }
      const msg = err.response?.data?.message ?? err.message ?? '请求失败'
      ElMessage.error(msg)
      return Promise.reject(err)
    },
  )
}
