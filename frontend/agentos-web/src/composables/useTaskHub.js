import { ref } from 'vue'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'

const apiKey = import.meta.env.VITE_DEV_API_KEY

/**
 * UI-only push channel. On (re)connect the caller must refetch REST data because the
 * database is the source of truth and SignalR is best effort.
 */
export function useTaskHub(onChanged) {
  const connected = ref(false)
  let connection = null

  async function start() {
    if (!apiKey) return

    connection = new HubConnectionBuilder()
      .withUrl(`/hubs/tasks?api_key=${encodeURIComponent(apiKey)}`)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    const notify = () => onChanged()
    connection.on('taskChanged', notify)
    connection.on('tasksClaimed', notify)
    connection.onreconnected(() => {
      connected.value = true
      notify()
    })
    connection.onclose(() => {
      connected.value = false
    })

    try {
      await connection.start()
      connected.value = true
    } catch {
      connected.value = false
    }
  }

  async function stop() {
    if (connection) {
      await connection.stop()
      connection = null
    }
    connected.value = false
  }

  return { connected, start, stop }
}
