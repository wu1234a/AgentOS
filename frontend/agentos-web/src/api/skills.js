import http from './http'

export function listSkills({ projectId, code, includeDisabled = false, limit = 100, offset = 0 } = {}) {
  return http
    .get('/api/v1/skills', { params: { projectId, code, includeDisabled, limit, offset } })
    .then((response) => response.data)
}

export function createSkill(payload) {
  return http.post('/api/v1/skills', payload).then((response) => response.data)
}

export function updateSkill(skillId, payload) {
  return http.put(`/api/v1/skills/${skillId}`, payload).then((response) => response.data)
}

export function enableSkill(skillId) {
  return http.post(`/api/v1/skills/${skillId}/enable`).then((response) => response.data)
}

export function disableSkill(skillId) {
  return http.post(`/api/v1/skills/${skillId}/disable`).then((response) => response.data)
}

export function getSkillContent(skillId) {
  return http.get(`/api/v1/skills/${skillId}/content`).then((response) => response.data)
}
