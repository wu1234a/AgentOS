import http from './http'

export function listCodingRuns({ status, limit = 50, offset = 0 } = {}) {
  return http
    .get('/api/v1/coding-runs', { params: { status, limit, offset } })
    .then((response) => response.data)
}

export function getCodingRun(runId) {
  return http.get(`/api/v1/coding-runs/${runId}`).then((response) => response.data)
}

export function createCodingRun(payload) {
  return http.post('/api/v1/coding-runs', payload).then((response) => response.data)
}

export function cancelCodingRun(runId) {
  return http.post(`/api/v1/coding-runs/${runId}/cancel`).then((response) => response.data)
}

export function retryCodingRun(runId) {
  return http.post(`/api/v1/coding-runs/${runId}/retry`).then((response) => response.data)
}
