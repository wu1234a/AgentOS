<script setup>
import { ref } from 'vue'
import { useRoute } from 'vue-router'
import { Connection, Cpu, Document, List, MagicStick, Monitor, Notebook, Refresh, SetUp, User } from '@element-plus/icons-vue'

const route = useRoute()
const collapsed = ref(false)
</script>

<template>
  <el-container class="app-shell">
    <el-aside :width="collapsed ? '64px' : '220px'" class="app-aside">
      <div class="brand">AgentOS</div>
      <el-menu :default-active="route.path" :collapse="collapsed" router class="app-menu">
        <el-menu-item index="/">
          <el-icon><Monitor /></el-icon>
          <template #title>Dashboard</template>
        </el-menu-item>
        <el-menu-item index="/events">
          <el-icon><Document /></el-icon>
          <template #title>Events</template>
        </el-menu-item>
        <el-menu-item index="/tasks">
          <el-icon><List /></el-icon>
          <template #title>Tasks</template>
        </el-menu-item>
        <el-menu-item index="/planner">
          <el-icon><MagicStick /></el-icon>
          <template #title>Planner</template>
        </el-menu-item>
        <el-menu-item index="/skills">
          <el-icon><Notebook /></el-icon>
          <template #title>Skills</template>
        </el-menu-item>
        <el-menu-item index="/models">
          <el-icon><Connection /></el-icon>
          <template #title>Models</template>
        </el-menu-item>
        <el-menu-item index="/agents">
          <el-icon><User /></el-icon>
          <template #title>Agents</template>
        </el-menu-item>
        <el-menu-item index="/runners">
          <el-icon><Cpu /></el-icon>
          <template #title>Runners</template>
        </el-menu-item>
        <el-menu-item index="/rules">
          <el-icon><SetUp /></el-icon>
          <template #title>Rules</template>
        </el-menu-item>
      </el-menu>
      <div class="aside-footer">
        <el-button text @click="collapsed = !collapsed">
          <el-icon><Refresh /></el-icon>
        </el-button>
      </div>
    </el-aside>
    <el-container>
      <el-header class="app-header">
        <span class="app-title">AgentOS 管理后台</span>
      </el-header>
      <el-main class="app-main">
        <router-view />
      </el-main>
    </el-container>
  </el-container>
</template>

<style scoped>
.app-shell {
  min-height: 100vh;
}

.app-aside {
  display: flex;
  flex-direction: column;
  background: #1f2937;
  color: #f9fafb;
  transition: width 0.2s ease;
}

.brand {
  padding: 18px 20px;
  font-size: 18px;
  font-weight: 700;
  letter-spacing: 0.04em;
  color: #f9fafb;
}

.app-menu {
  border-right: none;
  background: transparent;
}

.app-menu :deep(.el-menu-item) {
  color: #d1d5db;
}

.app-menu :deep(.el-menu-item.is-active) {
  color: #ffffff;
  background: #374151;
}

.aside-footer {
  margin-top: auto;
  padding: 12px;
  display: flex;
  justify-content: center;
}

.app-header {
  display: flex;
  align-items: center;
  background: #ffffff;
  border-bottom: 1px solid #e5e7eb;
}

.app-title {
  font-size: 16px;
  font-weight: 600;
}

.app-main {
  background: #f3f4f6;
}
</style>
