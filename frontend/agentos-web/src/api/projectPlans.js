import http from './http'

export function listProjectPlans({ status, limit = 50 } = {}) {
  return http
    .get('/api/v1/project-plans', { params: { status, limit } })
    .then((response) => response.data)
}

export function getProjectPlan(planId) {
  return http.get(`/api/v1/project-plans/${planId}`).then((response) => response.data)
}

export function createProjectPlan(payload) {
  return http
    .post('/api/v1/project-plans', payload, { timeout: 60000 })
    .then((response) => response.data)
}

export function approveProjectPlan(planId) {
  return http.post(`/api/v1/project-plans/${planId}/approve`).then((response) => response.data)
}

export function rejectProjectPlan(planId) {
  return http.post(`/api/v1/project-plans/${planId}/reject`).then((response) => response.data)
}

export function approveProjectPlanRelease(planId) {
  return http.post(`/api/v1/project-plans/${planId}/approve-release`).then((response) => response.data)
}

export function releaseProjectPlan(planId) {
  return http.post(`/api/v1/project-plans/${planId}/release`).then((response) => response.data)
}

export function reviseProjectPlan(planId, feedback) {
  return http.post(`/api/v1/project-plans/${planId}/revise`, { feedback }).then((response) => response.data)
}

export function getProjectPlanProgress(planId) {
  return http.get(`/api/v1/project-plans/${planId}/progress`).then((response) => response.data)
}
