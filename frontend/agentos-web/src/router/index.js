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
import ModelProfilesView from '../views/ModelProfilesView.vue'
import SkillsView from '../views/SkillsView.vue'
import PlannerView from '../views/PlannerView.vue'
import CodingRunsView from '../views/CodingRunsView.vue'
import ApprovalsView from '../views/ApprovalsView.vue'
import ProjectsView from '../views/ProjectsView.vue'
import UsageView from '../views/UsageView.vue'

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
      path: '/planner',
      name: 'planner',
      component: PlannerView,
      meta: { title: 'Planner' }
    },
    {
      path: '/approvals',
      name: 'approvals',
      component: ApprovalsView,
      meta: { title: 'Approvals' }
    },
    {
      path: '/coding-runs',
      name: 'coding-runs',
      component: CodingRunsView,
      meta: { title: 'Coding Runs' }
    },
    {
      path: '/skills',
      name: 'skills',
      component: SkillsView,
      meta: { title: 'Skills' }
    },
    {
      path: '/models',
      name: 'model-profiles',
      component: ModelProfilesView,
      meta: { title: 'Model Profiles' }
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
      path: '/projects',
      name: 'projects',
      component: ProjectsView,
      meta: { title: 'Projects' }
    },
    {
      path: '/usage',
      name: 'usage',
      component: UsageView,
      meta: { title: 'Usage' }
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
