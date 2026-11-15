import http from './http'

const actorOptions = { headers: { 'X-AgentOS-Actor': 'web-admin' } }

export function listProjectMilestones(projectId) {
  return http.get(`/api/v1/projects/${projectId}/milestones`).then((response) => response.data)
}

export function verifyProjectMilestone(projectId, milestoneId) {
  return http
    .post(`/api/v1/projects/${projectId}/milestones/${milestoneId}/verify`, null, actorOptions)
    .then((response) => response.data)
}

export function acceptProject(projectId) {
  return http
    .post(`/api/v1/projects/${projectId}/accept`, null, actorOptions)
    .then((response) => response.data)
}
