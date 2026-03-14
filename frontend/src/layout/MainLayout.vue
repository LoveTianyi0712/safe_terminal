<template>
  <el-container class="main-container">
    <!-- 侧边栏 -->
    <el-aside :width="isCollapsed ? '64px' : '220px'" class="sidebar">
      <div class="logo">
        <el-icon v-if="isCollapsed" size="28"><Monitor /></el-icon>
        <span v-else>终端安全监控</span>
      </div>
      <el-menu
        :default-active="route.path"
        :collapse="isCollapsed"
        router
        background-color="#1a1f2e"
        text-color="#adb5bd"
        active-text-color="#409EFF"
      >
        <el-menu-item index="/dashboard">
          <el-icon><Odometer /></el-icon>
          <template #title>仪表盘</template>
        </el-menu-item>
        <el-menu-item index="/terminals">
          <el-icon><Monitor /></el-icon>
          <template #title>终端管理</template>
        </el-menu-item>
        <el-menu-item index="/events">
          <el-icon><Search /></el-icon>
          <template #title>事件检索</template>
        </el-menu-item>
        <el-menu-item index="/alerts">
          <el-icon><Bell /></el-icon>
          <template #title>
            告警中心
            <el-badge v-if="alertStore.openCount > 0" :value="alertStore.openCount" />
          </template>
        </el-menu-item>
        <el-menu-item index="/policies">
          <el-icon><Setting /></el-icon>
          <template #title>策略配置</template>
        </el-menu-item>
      </el-menu>
    </el-aside>

    <!-- 主区域 -->
    <el-container>
      <el-header class="header">
        <el-icon class="collapse-btn" @click="isCollapsed = !isCollapsed">
          <Fold v-if="!isCollapsed" /><Expand v-else />
        </el-icon>
        <div class="header-right">
          <el-badge :value="alertStore.openCount" :hidden="alertStore.openCount === 0">
            <el-icon size="20"><Bell /></el-icon>
          </el-badge>
          <el-dropdown style="margin-left:16px">
            <span class="user-info">
              <el-avatar size="small" :icon="UserFilled" />
              <span class="username">{{ userStore.username }}</span>
              <el-tag size="small" :type="userStore.isAdmin ? 'danger' : 'info'" style="margin-left:6px">
                {{ userStore.role }}
              </el-tag>
            </span>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item disabled>{{ userStore.username }}</el-dropdown-item>
                <el-dropdown-item divided @click="logout">退出登录</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>
      </el-header>
      <el-main>
        <router-view />
      </el-main>
    </el-container>
  </el-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { UserFilled } from '@element-plus/icons-vue'
import { useAlertStore } from '@/stores/alertStore'
import { useUserStore }  from '@/stores/userStore'
import { useAlertWebSocket } from '@/composables/useWebSocket'

const route      = useRoute()
const router     = useRouter()
const alertStore = useAlertStore()
const userStore  = useUserStore()
const isCollapsed = ref(false)

const { connect } = useAlertWebSocket((alert) => {
  alertStore.addRealtimeAlert(alert)
})

onMounted(() => {
  alertStore.fetchStats()
  userStore.fetchMe()
  connect()
})

function logout() {
  userStore.clearUser()
  router.push('/login')
}
</script>

<style scoped>
.main-container { height: 100vh; }
.sidebar {
  background: #1a1f2e;
  transition: width 0.3s;
  overflow: hidden;
}
.logo {
  height: 60px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
  font-size: 16px;
  font-weight: 600;
  border-bottom: 1px solid #2d3448;
}
.header {
  background: #fff;
  display: flex;
  align-items: center;
  border-bottom: 1px solid #e8eaf0;
  padding: 0 16px;
}
.collapse-btn { cursor: pointer; font-size: 20px; }
.header-right { margin-left: auto; display: flex; align-items: center; }
.user-info    { display: flex; align-items: center; gap: 6px; cursor: pointer; }
.username     { font-size: 14px; color: #303133; }
</style>
