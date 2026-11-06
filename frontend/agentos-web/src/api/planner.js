import http from './http'

export function listDrafts({ status, limit = 50, offset = 0 } = {}) {
  return http
    .get('/api/v1/planner/drafts', { params: { status, limit, offset } })
    .then((response) => response.data)
}

export function createDraft(payload) {
  return http.post('/api/v1/planner/drafts', payload, { timeout: 120000 }).then((response) => response.data)
}

export function approveDraft(draftId) {
  return http.post(`/api/v1/planner/drafts/${draftId}/approve`).then((response) => response.data)
}

export function rejectDraft(draftId) {
  return http.post(`/api/v1/planner/drafts/${draftId}/reject`).then((response) => response.data)
}
