import { useState } from 'react'
import { criarPedido } from '../api/client'
import { useCart } from '../context/CartContext'
import { useCliente } from '../context/ClienteContext'
import { usePedidos } from '../context/PedidosContext'
import { formatBRL } from '../utils/format'
import { isEmailValido } from '../utils/validation'

export default function Carrinho({ navigate }) {
  const { items, setQuantidade, removeItem, clear, totalItens, totalValor } = useCart()
  const { clienteId, setClienteId } = useCliente()
  const { addPedido, iniciarCheckout } = usePedidos()

  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState(null)
  const [sucesso, setSucesso] = useState(null)

  async function finalizar() {
    setErro(null)
    setSucesso(null)
    const cliente = clienteId.trim()
    if (!cliente) {
      setErro('Informe seu e-mail para identificar o pedido.')
      return
    }
    if (!isEmailValido(cliente)) {
      setErro('Informe um e-mail válido (ex.: cliente@email.com).')
      return
    }

    if (enviando || items.length === 0) return
    const checkout = iniciarCheckout()
    setEnviando(true)
    try {
      const itensPayload = items.map((i) => ({ id: i.id, quantidade: i.quantidade }))
      const resp = await criarPedido({ clienteId: cliente, itens: itensPayload })
      const pedidoId = resp?.pedidoId ?? null
      if (!pedidoId) throw new Error('O servidor não retornou a identificação do pedido.')
      checkout.associar(pedidoId)
      const valorTotal = Number.isFinite(resp?.totalValue) ? resp.totalValue : totalValor

      addPedido({
        pedidoId,
        clienteId: cliente,
        valorTotal,
        itens: items.map((i) => ({ id: i.id, descricao: i.descricao, quantidade: i.quantidade })),
      })
      clear()
      setSucesso({ pedidoId, valorTotal })
    } catch (e) {
      checkout.cancelar()
      setErro(e.message)
    } finally {
      setEnviando(false)
    }
  }

  if (sucesso) {
    return (
      <section>
        <h2 className="mb-4 text-lg font-semibold">Pedido criado</h2>
        <div className="rounded-lg border border-green-200 bg-green-50 p-4 text-sm text-green-800">
          <p className="font-medium">Seu pedido foi enviado para processamento. ✅</p>
          <p className="mt-1">
            Pedido <span className="font-mono">{sucesso.pedidoId ?? '—'}</span> ·{' '}
            {formatBRL(sucesso.valorTotal)}
          </p>
          <p className="mt-2 text-green-700">
            Acompanhe o status (estoque, pagamento e entrega) em tempo real na aba
            <strong> Meus Pedidos</strong>. A aba de pagamento abrirá quando o checkout
            estiver disponível. Se o navegador bloquear a abertura, use o botão
            “Abrir pagamento em nova aba” em Meus Pedidos.
          </p>
        </div>
        <div className="mt-4 flex gap-2">
          <button
            type="button"
            onClick={() => navigate?.('pedidos')}
            className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700"
          >
            Ir para Meus Pedidos
          </button>
          <button
            type="button"
            onClick={() => setSucesso(null)}
            className="rounded-md border border-slate-300 px-4 py-2 text-sm text-slate-600 hover:bg-slate-100"
          >
            Voltar ao carrinho
          </button>
        </div>
      </section>
    )
  }

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
              <p className="text-sm text-slate-500">{formatBRL(item.preco)} · un.</p>
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

      <div className="mt-4 rounded-lg border border-slate-200 bg-white p-4">
        <label htmlFor="clienteId" className="block text-sm font-medium text-slate-700">
          Seu e-mail (identificação do cliente)
        </label>
        <input
          id="clienteId"
          type="email"
          value={clienteId}
          onChange={(e) => setClienteId(e.target.value)}
          placeholder="cliente@email.com"
          className="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500"
        />

        {erro && (
          <p className="mt-3 rounded-md bg-red-50 p-2 text-sm text-red-700">{erro}</p>
        )}

        <button
          type="button"
          onClick={finalizar}
          disabled={enviando}
          className="mt-4 w-full rounded-md bg-blue-600 px-4 py-3 font-medium text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:bg-slate-300"
        >
          {enviando ? 'Enviando…' : 'Finalizar pedido'}
        </button>
      </div>
    </section>
  )
}
