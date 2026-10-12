import { useCallback, useEffect, useState } from 'react'
import { getCatalogo } from '../api/client'
import { useCart } from '../context/CartContext'
import { formatBRL } from '../utils/format'

export default function Catalogo() {
  const [produtos, setProdutos] = useState([])
  const [loading, setLoading] = useState(true)
  const [erro, setErro] = useState(null)
  const { addItem } = useCart()

  const carregar = useCallback(() => {
    setLoading(true)
    setErro(null)
    getCatalogo()
      .then((data) => setProdutos(Array.isArray(data) ? data : []))
      .catch((e) => setErro(e.message))
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    // Fetch on mount is an intentional external-data sync.
    // oxlint-disable-next-line react/set-state-in-effect
    carregar()
  }, [carregar])

  return (
    <section>
      <div className="mb-4 flex items-center justify-between">
        <h2 className="text-lg font-semibold">Catálogo</h2>
        <button
          type="button"
          onClick={carregar}
          className="rounded-md border border-slate-300 px-3 py-1.5 text-sm text-slate-600 hover:bg-slate-100"
        >
          Atualizar
        </button>
      </div>

      {loading && <p className="text-sm text-slate-500">Carregando produtos…</p>}

      {erro && !loading && (
        <div className="rounded-md border border-red-200 bg-red-50 p-4 text-sm text-red-700">
          <p className="font-medium">Não foi possível carregar o catálogo.</p>
          <p className="mt-1 text-red-600">{erro}</p>
          <button
            type="button"
            onClick={carregar}
            className="mt-3 rounded-md bg-red-600 px-3 py-1.5 text-white hover:bg-red-700"
          >
            Tentar novamente
          </button>
        </div>
      )}

      {!loading && !erro && produtos.length === 0 && (
        <p className="text-sm text-slate-500">Nenhum produto disponível no momento.</p>
      )}

      {!loading && !erro && produtos.length > 0 && (
        <ul className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {produtos.map((produto) => {
            const semEstoque = produto.quantidadeDisponivel <= 0
            return (
              <li
                key={produto.id}
                className="flex flex-col rounded-lg border border-slate-200 bg-white p-4 shadow-sm"
              >
                <div className="flex items-start justify-between">
                  <h3 className="font-medium">{produto.descricao ?? `Produto ${produto.id}`}</h3>
                  <span className="rounded bg-slate-100 px-2 py-0.5 text-xs text-slate-500">
                    #{produto.id}
                  </span>
                </div>
                <p className="mt-2 text-xl font-semibold text-blue-700">
                  {formatBRL(produto.preco)}
                </p>
                <p
                  className={`mt-1 text-sm ${semEstoque ? 'text-red-600' : 'text-slate-500'}`}
                >
                  {semEstoque
                    ? 'Sem estoque'
                    : `${produto.quantidadeDisponivel} em estoque`}
                </p>
                <button
                  type="button"
                  disabled={semEstoque}
                  onClick={() => addItem(produto)}
                  className="mt-4 rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:bg-slate-300"
                >
                  Adicionar ao carrinho
                </button>
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
