import { createContext, useContext, useEffect, useMemo, useState } from 'react'

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

  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(pedidos))
    } catch {
      /* storage indisponível — ignora */
    }
  }, [pedidos])

  const addPedido = (pedido) =>
    setPedidos((prev) => [{ ...pedido, criadoEm: new Date().toISOString() }, ...prev])

  const value = useMemo(() => ({ pedidos, addPedido }), [pedidos])
  return <PedidosContext.Provider value={value}>{children}</PedidosContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function usePedidos() {
  const ctx = useContext(PedidosContext)
  if (!ctx) throw new Error('usePedidos deve ser usado dentro de <PedidosProvider>')
  return ctx
}
