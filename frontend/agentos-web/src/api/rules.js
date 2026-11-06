import http from './http'

export async function fetchRules(params = {}) {
  const response = await http.get('/api/v1/rules', { params })
  return response.data
}

export async function fetchRule(id) {
  const response = await http.get(`/api/v1/rules/${id}`)
  return response.data
}

export async function createRule(payload) {
  const response = await http.post('/api/v1/rules', payload)
  return response.data
}

export async function updateRule(id, payload) {
  const response = await http.put(`/api/v1/rules/${id}`, payload)
  return response.data
}

export async function enableRule(id) {
  const response = await http.post(`/api/v1/rules/${id}/enable`)
  return response.data
}

export async function disableRule(id) {
  const response = await http.post(`/api/v1/rules/${id}/disable`)
  return response.data
}

export async function validateRule(payload) {
  const response = await http.post('/api/v1/rules/validate', payload)
  return response.data
}
