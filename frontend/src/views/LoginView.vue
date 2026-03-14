<template>
  <div class="login-page">
    <el-card class="login-card" shadow="always">
      <div class="login-header">
        <el-icon :size="48" color="#409EFF"><Monitor /></el-icon>
        <h2>终端安全监控系统</h2>
        <p>Safe Terminal Monitor</p>
      </div>
      <el-form :model="form" :rules="rules" ref="formRef" label-position="top">
        <el-form-item label="用户名" prop="username">
          <el-input v-model="form.username" placeholder="请输入用户名" prefix-icon="User" />
        </el-form-item>
        <el-form-item label="密码" prop="password">
          <el-input v-model="form.password" type="password" placeholder="请输入密码"
            prefix-icon="Lock" show-password @keyup.enter="login" />
        </el-form-item>
        <el-button type="primary" :loading="loading" style="width:100%;margin-top:8px" @click="login">
          登 录
        </el-button>
      </el-form>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { http } from '@/api/http'
import { useUserStore } from '@/stores/userStore'

interface LoginResponse {
  token:        string
  refreshToken: string
  expiresAt:    number
  username:     string
  role:         string
}

const router    = useRouter()
const userStore = useUserStore()
const loading   = ref(false)
const formRef   = ref()

const form = reactive({ username: 'admin', password: '' })
const rules = {
  username: [{ required: true, message: '请输入用户名', trigger: 'blur' }],
  password: [{ required: true, message: '请输入密码',   trigger: 'blur' }],
}

async function login() {
  await formRef.value?.validate()
  loading.value = true
  try {
    const res = await http.post<LoginResponse>('/auth/login', form) as unknown as LoginResponse

    // 持久化 Token
    localStorage.setItem('token',        res.token)
    localStorage.setItem('refreshToken', res.refreshToken)
    localStorage.setItem('expiresAt',    String(res.expiresAt))

    // 写入用户 Store
    userStore.setUser(res.username, res.role)

    router.push('/dashboard')
  } catch (err: any) {
    const msg = err?.response?.data?.message ?? '用户名或密码错误'
    ElMessage.error(msg)
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.login-page {
  min-height: 100vh;
  background: linear-gradient(135deg, #1a1f2e 0%, #2d3448 100%);
  display: flex;
  align-items: center;
  justify-content: center;
}
.login-card   { width: 400px; border-radius: 12px; }
.login-header { text-align: center; margin-bottom: 24px; }
.login-header h2 { margin: 12px 0 4px; font-size: 22px; }
.login-header p  { color: #909399; font-size: 13px; margin: 0; }
</style>
