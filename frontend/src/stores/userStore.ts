import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { http } from '@/api/http'

let _refreshTimer: ReturnType<typeof setInterval> | null = null

export const useUserStore = defineStore('user', () => {
  const username = ref(localStorage.getItem('username') ?? '')
  const role     = ref(localStorage.getItem('role')     ?? '')

  // ─── 计算属性：权限判断 ───────────────────────────────────────────────
  const isAdmin    = computed(() => role.value === 'ADMIN')
  const isOperator = computed(() => ['ADMIN', 'OPERATOR'].includes(role.value))
  const isLoggedIn = computed(() => !!localStorage.getItem('token'))

  /**
   * 从 JWT Payload 中解析用户信息（Base64 解码，无需网络请求）。
   * 适用于页面刷新后快速恢复用户状态，避免等待 /auth/me 响应。
   * 若 Token 不存在或格式错误则静默失败。
   */
  function setFromToken(token?: string) {
    const raw = token ?? localStorage.getItem('token')
    if (!raw) return
    try {
      const payload = JSON.parse(atob(raw.split('.')[1]))
      const name    = (payload.sub   as string) ?? ''
      const r       = (payload.role  as string) ?? ''
      // expiresAt 写回 localStorage 以便过期检查（后端 exp 是秒级时间戳）
      if (payload.exp) {
        localStorage.setItem('expiresAt', String(payload.exp))
      }
      setUser(name, r)
    } catch {
      // token 格式异常，不处理
    }
  }

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
    stopRefreshTimer()
  }

  // ─── 从服务端获取当前用户信息（页面刷新后恢复最新状态）──────────────
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
    const expiresAt    = Number(localStorage.getItem('expiresAt') ?? 0)
    const refreshToken = localStorage.getItem('refreshToken')
    if (!refreshToken) return

    const nowSec = Math.floor(Date.now() / 1000)
    // 距过期不足 5 分钟时刷新
    if (expiresAt > 0 && expiresAt - nowSec < 300) {
      try {
        const res = await http.post<any>('/auth/refresh', { refreshToken })
        localStorage.setItem('token',     (res as any).token)
        localStorage.setItem('expiresAt', String((res as any).expiresAt))
      } catch {
        clearUser()
      }
    }
  }

  /**
   * 启动自动刷新定时器（每 4 分钟检查一次 Token 是否快到期）。
   * 应在用户登录成功或 MainLayout 挂载后调用。
   */
  function startRefreshTimer() {
    if (_refreshTimer) return
    _refreshTimer = setInterval(() => {
      refreshTokenIfNeeded()
    }, 4 * 60 * 1000)
  }

  function stopRefreshTimer() {
    if (_refreshTimer) {
      clearInterval(_refreshTimer)
      _refreshTimer = null
    }
  }

  return {
    username, role,
    isAdmin, isOperator, isLoggedIn,
    setUser, setFromToken, clearUser,
    fetchMe, refreshTokenIfNeeded,
    startRefreshTimer, stopRefreshTimer,
  }
}, { persist: false })
