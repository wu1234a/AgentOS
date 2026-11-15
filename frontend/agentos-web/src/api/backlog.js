import http from './http'

export function listBacklog({ status, limit = 200 } = {}) {
  return http
    .get('/api/v1/backlog', { params: { status, limit } })
    .then((response) => response.data)
}
