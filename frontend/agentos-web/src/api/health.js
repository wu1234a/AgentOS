import http from './http'

export async function fetchLive() {
  const response = await http.get('/health/live', { validateStatus: () => true })
  return { ...response.data, httpStatus: response.status }
}

export async function fetchReady() {
  const response = await http.get('/health/ready', { validateStatus: () => true })
  return { ...response.data, httpStatus: response.status }
}
