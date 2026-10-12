import { useCart } from '../context/CartContext'
import { formatBRL } from '../utils/format'

export default function Carrinho() {
  const { items, setQuantidade, removeItem, clear, totalItens, totalValor } = useCart()

  if (items.length === 0) {
    return (
      <section>
        <h2 className="mb-2 text-lg font-semibold">Carrinho</h2>
        <p className="text-sm text-slate-500">
          Seu carrinho está vazio. Adicione produtos pelo catálogo.
        </p>
      </section>
    )
  }

  return (
    <section>
      <div className="mb-4 flex items-center justify-between">
        <h2 className="text-lg font-semibold">Carrinho</h2>
        <button
          type="button"
          onClick={clear}
          className="text-sm text-slate-500 hover:text-red-600"
        >
          Esvaziar
        </button>
      </div>

      <ul className="divide-y divide-slate-200 rounded-lg border border-slate-200 bg-white">
        {items.map((item) => (
          <li key={item.id} className="flex items-center gap-4 p-4">
            <div className="flex-1">
              <p className="font-medium">{item.descricao ?? `Produto ${item.id}`}</p>
              <p className="text-sm text-slate-500">
                {formatBRL(item.preco)} · un.
              </p>
            </div>

            <div className="flex items-center gap-1">
              <button
                type="button"
                aria-label="Diminuir"
                onClick={() => setQuantidade(item.id, item.quantidade - 1)}
                className="h-8 w-8 rounded-md border border-slate-300 text-slate-600 hover:bg-slate-100"
              >
                −
              </button>
              <span className="w-8 text-center tabular-nums">{item.quantidade}</span>
              <button
                type="button"
                aria-label="Aumentar"
                disabled={item.quantidade >= item.estoque}
                onClick={() => setQuantidade(item.id, item.quantidade + 1)}
                className="h-8 w-8 rounded-md border border-slate-300 text-slate-600 hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-40"
              >
                +
              </button>
            </div>

            <div className="w-24 text-right font-medium tabular-nums">
              {formatBRL(item.preco * item.quantidade)}
            </div>

            <button
              type="button"
              aria-label="Remover"
              onClick={() => removeItem(item.id)}
              className="text-slate-400 hover:text-red-600"
            >
              ✕
            </button>
          </li>
        ))}
      </ul>

      <div className="mt-4 flex items-center justify-between rounded-lg border border-slate-200 bg-white p-4">
        <span className="text-sm text-slate-500">
          {totalItens} {totalItens === 1 ? 'item' : 'itens'}
        </span>
        <div className="text-right">
          <p className="text-sm text-slate-500">Total</p>
          <p className="text-2xl font-semibold text-blue-700">{formatBRL(totalValor)}</p>
        </div>
      </div>

      <button
        type="button"
        disabled
        title="Disponível na próxima etapa"
        className="mt-4 w-full rounded-md bg-slate-300 px-4 py-3 font-medium text-white"
      >
        Finalizar pedido (Etapa 4)
      </button>
    </section>
  )
}
