import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { http } from '@/api/http'

export const useUserStore = defineStore('user', () => {
  const username = ref(localStorage.getItem('username') ?? '')
  const role     = ref(localStorage.getItem('role')     ?? '')

  // ─── 计算属性：权限判断 ───────────────────────────────────────────────
  const isAdmin    = computed(() => role.value === 'ADMIN')
  const isOperator = computed(() => ['ADMIN', 'OPERATOR'].includes(role.value))
  const isLoggedIn = computed(() => !!localStorage.getItem('token'))

  // ─── 登录成功后由 LoginView 调用 ──────────────────────────────────────
  function setUser(name: string, userRole: string) {
    username.value = name
    role.value     = userRole
    localStorage.setItem('username', name)
    localStorage.setItem('role',     userRole)
  }

  // ─── 退出登录 ────────────────────────────────────────────────────────
  function clearUser() {
    username.value = ''
    role.value     = ''
    localStorage.removeItem('token')
    localStorage.removeItem('refreshToken')
    localStorage.removeItem('expiresAt')
    localStorage.removeItem('username')
    localStorage.removeItem('role')
  }

  // ─── 从服务端获取当前用户信息（页面刷新后恢复状态）────────────────────
  async function fetchMe() {
    try {
      const res = await http.get<{ username: string; role: string }>('/auth/me')
      setUser((res as any).username, (res as any).role)
    } catch {
      clearUser()
    }
  }

  // ─── Token 自动刷新 ──────────────────────────────────────────────────
  async function refreshTokenIfNeeded() {
    const expiresAt   = Number(localStorage.getItem('expiresAt') ?? 0)
    const refreshToken = localStorage.getItem('refreshToken')
    if (!refreshToken) return

    // 距离过期不足 5 分钟时自动刷新
    const nowSec = Math.floor(Date.now() / 1000)
    if (expiresAt - nowSec < 300) {
      try {
        const res = await http.post<any>('/auth/refresh', { refreshToken })
        localStorage.setItem('token',     (res as any).token)
        localStorage.setItem('expiresAt', String((res as any).expiresAt))
      } catch {
        clearUser()
      }
    }
  }

  return {
    username, role,
    isAdmin, isOperator, isLoggedIn,
    setUser, clearUser, fetchMe, refreshTokenIfNeeded,
  }
}, { persist: false })
