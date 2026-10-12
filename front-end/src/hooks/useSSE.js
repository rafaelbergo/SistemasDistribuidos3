import { useEffect, useRef, useState } from 'react'

// Subscribes to a Server-Sent Events stream and accumulates parsed events.
// The browser's EventSource auto-reconnects on transient errors.
//
// Each incoming event's `data` is expected to be a JSON object, e.g.
//   { pedidoId, status, mensagem?, checkoutUrl?, timestamp? }
// Non-JSON payloads are kept under { raw }.
export function useSSE(url, { enabled = true } = {}) {
  const active = enabled && Boolean(url)
  const [events, setEvents] = useState([])
  // Derive the baseline from props; the effect only reports live transitions.
  const [status, setStatus] = useState(active ? 'connecting' : 'idle')
  const sourceRef = useRef(null)

  useEffect(() => {
    if (!active) return undefined

    const source = new EventSource(url)
    sourceRef.current = source

    source.onopen = () => setStatus('open')

    source.onmessage = (event) => {
      let data
      try {
        data = JSON.parse(event.data)
      } catch {
        data = { raw: event.data }
      }
      setEvents((prev) => [...prev, { ...data, receivedAt: new Date().toISOString() }])
    }

    // EventSource retries automatically; we only reflect the degraded state.
    source.onerror = () => setStatus('error')

    return () => {
      source.close()
      sourceRef.current = null
      setStatus(active ? 'connecting' : 'idle')
    }
  }, [url, active])

  const clear = () => setEvents([])

  return { events, status, clear }
}
