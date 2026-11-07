import http from './http'

export function getReview(codingRunId) {
  return http.get(`/api/v1/coding-runs/${codingRunId}/review`).then((response) => response.data)
}

export function createReview(codingRunId) {
  return http.post(`/api/v1/coding-runs/${codingRunId}/review`, null, { timeout: 620000 }).then((response) => response.data)
}
