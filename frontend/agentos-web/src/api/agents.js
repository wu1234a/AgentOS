import http from './http'

export function listAgents({ role, includeArchived = false, limit = 100, offset = 0 } = {}) {
  return http
    .get('/api/v1/agents', { params: { role, includeArchived, limit, offset } })
    .then((response) => response.data)
}

export function createAgent(payload) {
  return http.post('/api/v1/agents', payload).then((response) => response.data)
}

export function updateAgent(agentId, payload) {
  return http.put(`/api/v1/agents/${agentId}`, payload).then((response) => response.data)
}

export function enableAgent(agentId) {
  return http.post(`/api/v1/agents/${agentId}/enable`).then((response) => response.data)
}

export function disableAgent(agentId) {
  return http.post(`/api/v1/agents/${agentId}/disable`).then((response) => response.data)
}

export function archiveAgent(agentId) {
  return http.post(`/api/v1/agents/${agentId}/archive`).then((response) => response.data)
}
