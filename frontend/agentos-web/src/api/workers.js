import http from './http'

export function listWorkers(status) {
  return http.get('/api/v1/workers/admin', { params: { status } }).then((response) => response.data.items)
}

export function getWorkerStats() {
  return http.get('/api/v1/workers/stats').then((response) => response.data)
}

export function setWorkerEnabled(id, enabled) {
  return http.post(`/api/v1/workers/${id}/${enabled ? 'enable' : 'disable'}`).then((response) => response.data)
}
