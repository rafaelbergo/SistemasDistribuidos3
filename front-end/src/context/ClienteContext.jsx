import { createContext, useContext, useEffect, useMemo, useState } from 'react'

const ClienteContext = createContext(null)
const STORAGE_KEY = 'ecommerce.cliente'

// Holds the client identifier (e-mail) used to create orders and to open the
// per-client SSE stream. Persisted so it survives reloads.
export function ClienteProvider({ children }) {
  const [clienteId, setClienteId] = useState(() => {
    try {
      return localStorage.getItem(STORAGE_KEY) ?? ''
    } catch {
      return ''
    }
  })

  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, clienteId)
    } catch {
      /* storage indisponível — ignora */
    }
  }, [clienteId])

  const value = useMemo(() => ({ clienteId, setClienteId }), [clienteId])
  return <ClienteContext.Provider value={value}>{children}</ClienteContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useCliente() {
  const ctx = useContext(ClienteContext)
  if (!ctx) throw new Error('useCliente deve ser usado dentro de <ClienteProvider>')
  return ctx
}
