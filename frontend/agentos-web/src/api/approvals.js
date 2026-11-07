import http from './http'

export function listApprovals({ status, targetType, limit = 50, offset = 0 } = {}) {
  return http.get('/api/v1/approval-requests', { params: { status, targetType, limit, offset } }).then((response) => response.data)
}

export function createApproval(payload) {
  return http.post('/api/v1/approval-requests', payload).then((response) => response.data)
}

export function decideApproval(id, payload) {
  return http.post(`/api/v1/approval-requests/${id}/decision`, payload).then((response) => response.data)
}
