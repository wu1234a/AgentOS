import { createRouter, createWebHistory } from 'vue-router'
import DashboardView from '../views/DashboardView.vue'
import RulesView from '../views/RulesView.vue'
import RuleDetailView from '../views/RuleDetailView.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      name: 'dashboard',
      component: DashboardView,
      meta: { title: 'Dashboard' }
    },
    {
      path: '/rules',
      name: 'rules',
      component: RulesView,
      meta: { title: 'Rules' }
    },
    {
      path: '/rules/:id',
      name: 'rule-detail',
      component: RuleDetailView,
      meta: { title: 'Rule Detail' }
    }
  ]
})

export default router
