import { createRouter, createWebHistory } from 'vue-router'
import DashboardView from '../views/DashboardView.vue'
import RulesView from '../views/RulesView.vue'
import RuleDetailView from '../views/RuleDetailView.vue'
import EventsView from '../views/EventsView.vue'
import EventDetailView from '../views/EventDetailView.vue'
import TasksView from '../views/TasksView.vue'
import TaskDetailView from '../views/TaskDetailView.vue'
import RunnersView from '../views/RunnersView.vue'
import AgentsView from '../views/AgentsView.vue'

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
      path: '/events',
      name: 'events',
      component: EventsView,
      meta: { title: 'Events' }
    },
    {
      path: '/events/:id',
      name: 'event-detail',
      component: EventDetailView,
      meta: { title: 'Event Detail' }
    },
    {
      path: '/tasks',
      name: 'tasks',
      component: TasksView,
      meta: { title: 'Tasks' }
    },
    {
      path: '/tasks/:id',
      name: 'task-detail',
      component: TaskDetailView,
      meta: { title: 'Task Detail' }
    },
    {
      path: '/agents',
      name: 'agents',
      component: AgentsView,
      meta: { title: 'Agents' }
    },
    {
      path: '/runners',
      name: 'runners',
      component: RunnersView,
      meta: { title: 'Runners' }
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
