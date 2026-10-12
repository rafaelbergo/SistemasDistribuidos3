import { createContext, useContext, useEffect, useMemo, useState } from 'react'

const CartContext = createContext(null)
const STORAGE_KEY = 'ecommerce.cart'
const NO_LIMIT = Number.MAX_SAFE_INTEGER

function estoqueDe(produto) {
  return Number.isFinite(produto?.quantidadeDisponivel) ? produto.quantidadeDisponivel : NO_LIMIT
}

function loadInitial() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    const parsed = raw ? JSON.parse(raw) : []
    return Array.isArray(parsed) ? parsed : []
  } catch {
    return []
  }
}

export function CartProvider({ children }) {
  const [items, setItems] = useState(loadInitial)

  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(items))
    } catch {
      /* storage indisponível (ex.: aba privada) — ignora */
    }
  }, [items])

  const addItem = (produto, quantidade = 1) => {
    const estoque = estoqueDe(produto)
    setItems((prev) => {
      const existing = prev.find((i) => i.id === produto.id)
      if (existing) {
        const novaQtd = Math.min(existing.quantidade + quantidade, estoque)
        return prev.map((i) => (i.id === produto.id ? { ...i, quantidade: novaQtd } : i))
      }
      return [
        ...prev,
        {
          id: produto.id,
          descricao: produto.descricao,
          preco: produto.preco,
          estoque,
          quantidade: Math.min(quantidade, estoque),
        },
      ]
    })
  }

  const setQuantidade = (id, quantidade) => {
    setItems((prev) =>
      prev.flatMap((i) => {
        if (i.id !== id) return [i]
        const limite = Number.isFinite(i.estoque) ? i.estoque : NO_LIMIT
        const q = Math.max(0, Math.min(quantidade, limite))
        return q === 0 ? [] : [{ ...i, quantidade: q }]
      }),
    )
  }

  const removeItem = (id) => setItems((prev) => prev.filter((i) => i.id !== id))
  const clear = () => setItems([])

  const totalItens = useMemo(() => items.reduce((s, i) => s + i.quantidade, 0), [items])
  const totalValor = useMemo(() => items.reduce((s, i) => s + i.quantidade * i.preco, 0), [items])

  const value = useMemo(
    () => ({ items, addItem, setQuantidade, removeItem, clear, totalItens, totalValor }),
    [items, totalItens, totalValor],
  )

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useCart() {
  const ctx = useContext(CartContext)
  if (!ctx) throw new Error('useCart deve ser usado dentro de <CartProvider>')
  return ctx
}
