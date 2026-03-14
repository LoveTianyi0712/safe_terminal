import { ref, onUnmounted } from 'vue'
import SockJS from 'sockjs-client'
import { Client as StompClient } from '@stomp/stompjs'
import type { Alert } from '@/api/alert'

export function useAlertWebSocket(onAlert: (alert: Alert) => void) {
  const connected = ref(false)
  let client: StompClient | null = null

  function connect() {
    client = new StompClient({
      webSocketFactory: () => new SockJS('/ws'),
      reconnectDelay: 5000,
      onConnect: () => {
        connected.value = true
        // 订阅实时告警频道
        client!.subscribe('/topic/alerts', (msg) => {
          const alert: Alert = JSON.parse(msg.body)
          onAlert(alert)
        })
      },
      onDisconnect: () => {
        connected.value = false
      },
      onStompError: (frame) => {
        console.error('STOMP error', frame)
      },
    })
    client.activate()
  }

  function disconnect() {
    client?.deactivate()
    connected.value = false
  }

  onUnmounted(disconnect)

  return { connected, connect, disconnect }
}
