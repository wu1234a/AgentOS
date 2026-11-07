import http from './http'

export function getLatestQa(codingRunId) {
  return http.get(`/api/v1/coding-runs/${codingRunId}/qa-runs/latest`).then((response) => response.data)
}

export function createQa(codingRunId) {
  return http.post(`/api/v1/coding-runs/${codingRunId}/qa-runs`).then((response) => response.data)
}
