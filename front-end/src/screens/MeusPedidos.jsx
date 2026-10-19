import { useMemo, useState } from 'react'
import { useCliente } from '../context/ClienteContext'
import { usePedidos } from '../context/PedidosContext'
import { statusLabel, statusStyle } from '../api/status'
import { formatBRL } from '../utils/format'
import { isEmailValido } from '../utils/validation'

const CONN = {
  idle: { label: 'Sem conexão', dot: 'bg-slate-400' },
  connecting: { label: 'Conectando…', dot: 'bg-amber-400' },
  open: { label: 'Conectado (tempo real)', dot: 'bg-green-500' },
  error: { label: 'Reconectando…', dot: 'bg-red-500' },
}

function horaDe(ev) {
  const t = ev.timestamp ?? ev.receivedAt
  const d = t ? new Date(t) : null
  return d && !Number.isNaN(d.getTime()) ? d.toLocaleTimeString('pt-BR') : ''
}

export default function MeusPedidos() {
  const { clienteId, setClienteId } = useCliente()
  const { pedidos, events, status } = usePedidos()
  const [emailInput, setEmailInput] = useState('')
  const [erroEmail, setErroEmail] = useState(null)

  function acompanhar() {
    const mail = emailInput.trim()
    if (!isEmailValido(mail)) {
      setErroEmail('Informe um e-mail válido (ex.: cliente@email.com).')
      return
    }
    setErroEmail(null)
    setClienteId(mail)
  }

  const eventsByPedido = useMemo(() => {
    const map = new Map()
    for (const ev of events) {
      const id = ev.pedidoId ?? '—'
      if (!map.has(id)) map.set(id, [])
      map.get(id).push(ev)
    }
    return map
  }, [events])

  const orders = useMemo(() => {
    const byId = new Map()
    pedidos.filter((p) => p.clienteId?.toLowerCase() === clienteId.toLowerCase()).forEach((p, idx) => {
      byId.set(p.pedidoId ?? `local-${idx}`, { ...p })
    })
    for (const id of eventsByPedido.keys()) {
      if (id !== '—' && !byId.has(id)) byId.set(id, { pedidoId: id })
    }
    return Array.from(byId.entries()).map(([key, p]) => ({ key, ...p }))
  }, [pedidos, eventsByPedido, clienteId])

  if (!clienteId) {
    return (
      <section>
        <h2 className="mb-2 text-lg font-semibold">Meus Pedidos</h2>
        <p className="mb-4 text-sm text-slate-500">
          Informe seu e-mail para acompanhar seus pedidos em tempo real.
        </p>
        <div className="max-w-md">
          <div className="flex gap-2">
            <input
              type="email"
              value={emailInput}
              onChange={(e) => setEmailInput(e.target.value)}
              placeholder="cliente@email.com"
              className="flex-1 rounded-md border border-slate-300 px-3 py-2 text-sm outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500"
            />
            <button
              type="button"
              onClick={acompanhar}
              className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700"
            >
              Acompanhar
            </button>
          </div>
          {erroEmail && <p className="mt-2 text-sm text-red-600">{erroEmail}</p>}
        </div>
      </section>
    )
  }

  const conn = CONN[status] ?? CONN.idle

  return (
    <section>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-lg font-semibold">Meus Pedidos</h2>
        <div className="flex items-center gap-2 text-sm text-slate-500">
          <span className={`inline-block h-2.5 w-2.5 rounded-full ${conn.dot}`} />
          <span>{conn.label}</span>
          <span className="text-slate-300">·</span>
          <span className="font-mono text-xs">{clienteId}</span>
          <button
            type="button"
            onClick={() => setClienteId('')}
            className="ml-1 text-xs text-slate-400 hover:text-slate-700"
          >
            trocar
          </button>
        </div>
      </div>

      {orders.length === 0 && (
        <p className="text-sm text-slate-500">
          Você ainda não tem pedidos. Finalize um pedido no carrinho para acompanhá-lo aqui.
        </p>
      )}

      <div className="space-y-4">
        {orders.map((order) => {
          const evs = eventsByPedido.get(order.pedidoId) ?? []
          const ultimo = evs[evs.length - 1]
          const checkoutUrl = [...evs].reverse().find((e) => e.checkoutUrl)?.checkoutUrl
          const pago = evs.some((e) =>
            ['pagamento_aprovado', 'pagamento_recusado'].includes(e.status),
          )

          return (
            <article
              key={order.key}
              className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm"
            >
              <div className="flex flex-wrap items-start justify-between gap-2">
                <div>
                  <p className="text-sm text-slate-500">Pedido</p>
                  <p className="font-mono text-sm">{order.pedidoId ?? '—'}</p>
                </div>
                <div className="text-right">
                  {Number.isFinite(order.valorTotal) && (
                    <p className="font-semibold text-blue-700">
                      {formatBRL(order.valorTotal)}
                    </p>
                  )}
                  {ultimo ? (
                    <span
                      className={`mt-1 inline-block rounded-full px-2.5 py-0.5 text-xs font-medium ${statusStyle(
                        ultimo.status,
                      )}`}
                    >
                      {statusLabel(ultimo.status)}
                    </span>
                  ) : (
                    <span className="mt-1 inline-block rounded-full bg-slate-100 px-2.5 py-0.5 text-xs text-slate-500">
                      Aguardando atualizações…
                    </span>
                  )}
                </div>
              </div>

              {checkoutUrl && !pago && (
                <a
                  href={checkoutUrl}
                  target="_blank"
                  rel="noreferrer"
                  className="mt-3 inline-block rounded-md bg-amber-500 px-4 py-2 text-sm font-medium text-white hover:bg-amber-600"
                >
                  💳 Abrir pagamento em nova aba
                </a>
              )}

              {evs.length > 0 && (
                <ol className="mt-4 space-y-2 border-l-2 border-slate-100 pl-4">
                  {evs.map((ev, idx) => (
                    <li key={idx} className="relative">
                      <span
                        className={`absolute -left-[21px] top-1 h-2.5 w-2.5 rounded-full ${statusStyle(
                          ev.status,
                        ).split(' ')[0]}`}
                      />
                      <div className="flex items-center justify-between gap-2">
                        <span className="text-sm font-medium">{statusLabel(ev.status)}</span>
                        <span className="text-xs text-slate-400">{horaDe(ev)}</span>
                      </div>
                      {ev.mensagem && (
                        <p className="text-xs text-slate-500">{ev.mensagem}</p>
                      )}
                    </li>
                  ))}
                </ol>
              )}
            </article>
          )
        })}
      </div>
    </section>
  )
}
