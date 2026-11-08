import http from './http'

export function listProjects() {
  return http.get('/api/v1/projects').then((response) => response.data.items)
}

export function createProject(payload) {
  return http.post('/api/v1/projects', payload).then((response) => response.data)
}

export function updateProject(id, payload) {
  return http.patch(`/api/v1/projects/${id}`, payload).then((response) => response.data)
}

export function setProjectEnabled(id, enabled) {
  return http.post(`/api/v1/projects/${id}/${enabled ? 'enable' : 'disable'}`).then((response) => response.data)
}
