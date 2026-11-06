import http from './http'

export function listModelProfiles({ enabled, limit = 100, offset = 0 } = {}) {
  return http
    .get('/api/v1/model-profiles', { params: { enabled, limit, offset } })
    .then((response) => response.data)
}

export function createModelProfile(payload) {
  return http.post('/api/v1/model-profiles', payload).then((response) => response.data)
}

export function updateModelProfile(profileId, payload) {
  return http.put(`/api/v1/model-profiles/${profileId}`, payload).then((response) => response.data)
}

export function enableModelProfile(profileId) {
  return http.post(`/api/v1/model-profiles/${profileId}/enable`).then((response) => response.data)
}

export function disableModelProfile(profileId) {
  return http.post(`/api/v1/model-profiles/${profileId}/disable`).then((response) => response.data)
}
