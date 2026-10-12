// REST + SSE client for the API Gateway (MS.Principal).
// Base URL comes from VITE_API_URL (see .env), defaulting to the gateway's dev port.
export const BASE_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:5059'

export class ApiError extends Error {
  constructor(status, message) {
    super(message || `HTTP ${status}`)
    this.name = 'ApiError'
    this.status = status
  }
}

async function request(path, options = {}) {
  let res
  try {
    res = await fetch(`${BASE_URL}${path}`, {
      headers: { 'Content-Type': 'application/json', ...(options.headers || {}) },
      ...options,
    })
  } catch (err) {
    // Network error / gateway offline
    throw new ApiError(0, `Não foi possível conectar ao gateway (${BASE_URL}): ${err.message}`)
  }

  if (!res.ok) {
    let detail = ''
    try {
      detail = await res.text()
    } catch {
      detail = res.statusText
    }
    throw new ApiError(res.status, detail || res.statusText)
  }

  if (res.status === 204) return null
  const contentType = res.headers.get('content-type') || ''
  if (contentType.includes('application/json')) return res.json()
  const text = await res.text()
  return text.length ? text : null
}

// --- Catálogo & Pedidos (já expostos pelo backend) ---

// GET /api/catalogo -> [{ id, descricao, quantidadeDisponivel, preco }]
export function getCatalogo() {
  return request('/api/catalogo')
}

// POST /api/pedidos  body: { clienteId, itens: [{ id, quantidade }] }
// -> 202 { mensagem, pedidoId, totalValue }
export function criarPedido(pedido) {
  return request('/api/pedidos', {
    method: 'POST',
    body: JSON.stringify(pedido),
  })
}

// --- Promoções (contrato esperado; backend ainda vai expor) ---

// POST /api/interesse  body: { email, categorias: ["A", "B", ...] }
export function registrarInteresse(interesse) {
  return request('/api/interesse', {
    method: 'POST',
    body: JSON.stringify(interesse),
  })
}

// DELETE /api/interesse/{email}
export function cancelarInteresse(email) {
  return request(`/api/interesse/${encodeURIComponent(email)}`, {
    method: 'DELETE',
  })
}

// --- SSE (acompanhamento de pedidos; backend ainda vai expor) ---

// GET /api/pedidos/eventos?clienteId=... (text/event-stream)
export function pedidoEventsUrl(clienteId) {
  return `${BASE_URL}/api/pedidos/eventos?clienteId=${encodeURIComponent(clienteId)}`
}
