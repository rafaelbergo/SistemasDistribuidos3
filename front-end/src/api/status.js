// Maps the order-status values sent over SSE to user-facing labels and styles.
// The gateway forwards every state change it consumes from the backend
// microservices (Estoque, Pagamento, Entrega) to the connected client.
export const STATUS_LABELS = {
  estoque_confirmado: 'Estoque Confirmado',
  estoque_indisponivel: 'Estoque Indisponível',
  pagamento_pendente: 'Aguardando Pagamento',
  pagamento_aprovado: 'Pagamento Aprovado',
  pagamento_recusado: 'Pagamento Recusado',
  pedido_enviado: 'Pedido Enviado',
}

// Tailwind classes per status, used for badges/timeline dots.
export const STATUS_STYLES = {
  estoque_confirmado: 'bg-blue-100 text-blue-800',
  estoque_indisponivel: 'bg-red-100 text-red-800',
  pagamento_pendente: 'bg-amber-100 text-amber-800',
  pagamento_aprovado: 'bg-green-100 text-green-800',
  pagamento_recusado: 'bg-red-100 text-red-800',
  pedido_enviado: 'bg-emerald-100 text-emerald-800',
}

export function statusLabel(status) {
  return STATUS_LABELS[status] ?? status
}

export function statusStyle(status) {
  return STATUS_STYLES[status] ?? 'bg-slate-100 text-slate-700'
}
