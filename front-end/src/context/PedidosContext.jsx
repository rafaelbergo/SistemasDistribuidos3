import { createContext, useContext, useEffect, useMemo, useRef, useState } from 'react'
import { useCliente } from './ClienteContext'
import { pedidoEventsUrl } from '../api/client'
import { useSSE } from '../hooks/useSSE'
import { reservarCheckout } from '../utils/checkout'

const PedidosContext = createContext(null)
const STORAGE_KEY = 'ecommerce.pedidos'

function loadInitial() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    const parsed = raw ? JSON.parse(raw) : []
    return Array.isArray(parsed) ? parsed : []
  } catch {
    return []
  }
}

// Keeps a local history of orders created from this browser (pedidoId,
// valorTotal, itens, timestamp). The live status comes from SSE; this just
// gives Meus Pedidos something to show immediately after checkout.
export function PedidosProvider({ children }) {
  const [pedidos, setPedidos] = useState(loadInitial)
  const { clienteId } = useCliente()
  // Keep SSE alive while navigating between the cart and other screens.
  const { events, status } = useSSE(clienteId ? pedidoEventsUrl(clienteId) : null)
  const abas = useRef(new Map())

  useEffect(() => {
    for (const [pedidoId, checkout] of abas.current) {
      if (checkout.atualizar(events.filter((e) => e.pedidoId === pedidoId)))
        abas.current.delete(pedidoId)
    }
  }, [events, pedidos])

  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(pedidos))
    } catch {
      /* storage indisponível — ignora */
    }
  }, [pedidos])

  const addPedido = (pedido) =>
    setPedidos((prev) => [{ ...pedido, criadoEm: new Date().toISOString() }, ...prev])

  function iniciarCheckout() {
    const checkout = reservarCheckout()
    return {
      associar: (pedidoId) => abas.current.set(pedidoId, checkout),
      cancelar: () => checkout.cancelar(),
    }
  }

  const value = useMemo(() => ({ pedidos, addPedido, events, status, iniciarCheckout }), [pedidos, events, status])
  return <PedidosContext.Provider value={value}>{children}</PedidosContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function usePedidos() {
  const ctx = useContext(PedidosContext)
  if (!ctx) throw new Error('usePedidos deve ser usado dentro de <PedidosProvider>')
  return ctx
}
