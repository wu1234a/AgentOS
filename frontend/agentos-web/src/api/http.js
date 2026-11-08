import axios from 'axios'

const devApiKey = window.__AGENTOS_DEV_API_KEY__ || import.meta.env.VITE_DEV_API_KEY

const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || '',
  timeout: 10000,
  headers: {
    Accept: 'application/json',
    ...(devApiKey ? { 'X-AgentOS-Dev-Key': devApiKey } : {})
  }
})

http.interceptors.response.use(
  (response) => response,
  (error) => {
    const status = error.response?.status
    const detail = error.response?.data?.detail || error.response?.data?.title || error.message
    const message = status ? `HTTP ${status}: ${detail}` : detail
    const wrapped = new Error(message)
    wrapped.status = status
    wrapped.problem = error.response?.data
    return Promise.reject(wrapped)
  }
)

export default http
