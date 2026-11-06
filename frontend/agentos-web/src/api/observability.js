import http from './http'

export function getDashboard() {
  return http.get('/api/v1/dashboard/stats').then((response) => response.data)
}

export function listEvents({ page = 1, pageSize = 20 } = {}) {
  return http.get('/api/v1/events', { params: { page, pageSize } }).then((response) => response.data)
}

export function getEvent(eventId) {
  return http.get(`/api/v1/events/${eventId}`).then((response) => response.data)
}

export function getEventTasks(eventId) {
  return http.get(`/api/v1/events/${eventId}/tasks`).then((response) => response.data)
}

export function listTasks({ status, projectId, limit = 20, offset = 0 } = {}) {
  return http
    .get('/api/v1/tasks', { params: { status, projectId, limit, offset } })
    .then((response) => response.data)
}

export function getTask(taskId) {
  return http.get(`/api/v1/tasks/${taskId}`).then((response) => response.data)
}

export function getTaskLogs(taskId, { level, afterSequence = 0, limit = 100 } = {}) {
  return http
    .get(`/api/v1/tasks/${taskId}/logs`, { params: { level, afterSequence, limit } })
    .then((response) => response.data)
}

export function getAttemptLogs(taskId, attemptId, { level, afterSequence = 0, limit = 100 } = {}) {
  return http
    .get(`/api/v1/tasks/${taskId}/attempts/${attemptId}/logs`, {
      params: { level, afterSequence, limit }
    })
    .then((response) => response.data)
}

export function listWorkers() {
  return http.get('/api/v1/workers').then((response) => response.data)
}

export async function downloadArtifact(artifactId, name) {
  const response = await http.get(`/api/v1/artifacts/${artifactId}/download`, {
    responseType: 'blob'
  })
  const url = URL.createObjectURL(response.data)
  const link = document.createElement('a')
  link.href = url
  link.download = name
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}
