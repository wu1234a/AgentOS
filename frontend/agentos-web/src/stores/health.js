import { defineStore } from 'pinia'
import { ref } from 'vue'
import { fetchLive, fetchReady } from '../api/health'

export const useHealthStore = defineStore('health', () => {
  const live = ref(null)
  const ready = ref(null)
  const loading = ref(false)
  const error = ref(null)
  const lastCheckedAt = ref(null)

  async function refresh() {
    loading.value = true
    error.value = null

    try {
      const [liveResult, readyResult] = await Promise.all([fetchLive(), fetchReady()])
      live.value = liveResult
      ready.value = readyResult
      lastCheckedAt.value = new Date()
    } catch (exception) {
      error.value = exception.message
    } finally {
      loading.value = false
    }
  }

  return { live, ready, loading, error, lastCheckedAt, refresh }
})
