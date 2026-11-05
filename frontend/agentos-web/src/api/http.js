import axios from 'axios'

const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || '',
  timeout: 10000,
  headers: { Accept: 'application/json' }
})

http.interceptors.response.use(
  (response) => response,
  (error) => {
    const status = error.response?.status
    const detail = error.response?.data?.detail || error.response?.data?.title || error.message
    const message = status ? `HTTP ${status}: ${detail}` : detail
    return Promise.reject(new Error(message))
  }
)

export default http
