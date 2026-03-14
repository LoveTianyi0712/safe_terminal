import { createRouter, createWebHistory } from 'vue-router'

const routes = [
  {
    path: '/',
    component: () => import('@/layout/MainLayout.vue'),
    redirect: '/dashboard',
    children: [
      {
        path: 'dashboard',
        name: 'Dashboard',
        component: () => import('@/views/DashboardView.vue'),
        meta: { title: '仪表盘' },
      },
      {
        path: 'terminals',
        name: 'Terminals',
        component: () => import('@/views/TerminalListView.vue'),
        meta: { title: '终端管理' },
      },
      {
        path: 'terminals/:id',
        name: 'TerminalDetail',
        component: () => import('@/views/TerminalDetailView.vue'),
        meta: { title: '终端详情' },
      },
      {
        path: 'events',
        name: 'Events',
        component: () => import('@/views/EventSearchView.vue'),
        meta: { title: '事件检索' },
      },
      {
        path: 'alerts',
        name: 'Alerts',
        component: () => import('@/views/AlertCenterView.vue'),
        meta: { title: '告警中心' },
      },
      {
        path: 'policies',
        name: 'Policies',
        component: () => import('@/views/PolicyConfigView.vue'),
        meta: { title: '策略配置' },
      },
    ],
  },
  {
    path: '/login',
    name: 'Login',
    component: () => import('@/views/LoginView.vue'),
    meta: { title: '登录' },
  },
]

const router = createRouter({
  history: createWebHistory(),
  routes,
})

router.beforeEach((to) => {
  const token = localStorage.getItem('token')
  if (!token && to.name !== 'Login') {
    return { name: 'Login' }
  }
  document.title = `${to.meta.title ?? ''} - 终端安全监控`
})

export default router
