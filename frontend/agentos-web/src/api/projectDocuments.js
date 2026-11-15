import http from './http'

export function listProjectDocuments(projectId) {
  return http.get(`/api/v1/projects/${projectId}/documents`).then((response) => response.data)
}

export function getProjectDocument(projectId, documentId) {
  return http.get(`/api/v1/projects/${projectId}/documents/${documentId}`).then((response) => response.data)
}
