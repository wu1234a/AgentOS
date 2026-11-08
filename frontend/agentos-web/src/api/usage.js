import http from './http'

export function getUsageStatistics({ projectId, from, to } = {}) {
  return http.get('/api/v1/usage/statistics', { params: { projectId, from, to } }).then((response) => response.data)
}
