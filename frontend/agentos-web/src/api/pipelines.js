import http from './http'

export function listPipelines({ projectId, limit = 20 } = {}) {
  return http.get('/api/v1/pipelines', { params: { projectId, limit } }).then((response) => response.data.items)
}

export function getPipeline(taskId) {
  return http.get(`/api/v1/pipelines/tasks/${taskId}`).then((response) => response.data)
}
