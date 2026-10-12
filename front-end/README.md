# Front-end — e-Commerce (Trab2 Sistemas Distribuídos)

Interface web (React + Vite + Tailwind CSS) que consome o **API Gateway (MS.Principal)**
via **REST** e **SSE**. Corresponde ao componente *Frontend web* do Trab2.

## Como rodar

```bash
npm install
npm run dev      # servidor de desenvolvimento (Vite)
npm run build    # build de produção em dist/
```

A URL do gateway é configurada em `.env`:

```
VITE_API_URL=http://localhost:5059
```

## Funcionalidades (requisitos do PDF)

| Tela          | Requisito                                   | Etapa |
|---------------|---------------------------------------------|-------|
| Catálogo      | Consultar produtos disponíveis (REST)       | 3     |
| Carrinho      | Criar pedido (REST) + abrir aba de pagamento| 3 / 4 |
| Meus Pedidos  | Acompanhar status em tempo real (SSE)       | 4     |
| Promoções     | Inscrever/cancelar e-mail por categorias    | 5     |

## Contrato da API (Gateway / MS.Principal)

O front-end foi construído contra o contrato abaixo. ✅ = já existe no backend;
🔧 = **ainda precisa ser implementado no backend** (parte da dupla).

### ✅ `GET /api/catalogo`
Lista produtos disponíveis (gateway consulta o MS.Estoque via REST).
```json
[
  { "id": "A", "descricao": "Produto A", "quantidadeDisponivel": 4, "preco": 99.99 }
]
```

### ✅ `POST /api/pedidos`
Cria um pedido e publica `pedido.criado` no RabbitMQ.
```jsonc
// request
{ "clienteId": "cliente@email.com", "itens": [{ "id": "A", "quantidade": 2 }] }
// response 202
{ "mensagem": "Order created and sent to processing", "pedidoId": "ab12…", "totalValue": 199.98 }
```

### 🔧 `GET /api/pedidos/eventos?clienteId=...`  (SSE — `text/event-stream`)
Fluxo de notificações em tempo real do(s) pedido(s) do cliente. O gateway encaminha
cada mudança de estado que consome (`pedido.estoque_ok`, `estoque.indisponivel`,
`pagamento.aprovado`, `pagamento.recusado`, `pedido.enviado`).

Cada evento SSE deve ter `data` em JSON:
```jsonc
{
  "pedidoId": "ab12…",
  "status": "estoque_confirmado",   // ver valores abaixo
  "mensagem": "Itens reservados",
  "checkoutUrl": "http://localhost:6000/checkout/ab12…", // presente em pagamento_pendente
  "timestamp": "2026-10-12T14:30:00Z"
}
```
Valores de `status` esperados pelo front-end
(`src/api/status.js`): `estoque_confirmado`, `estoque_indisponivel`,
`pagamento_pendente`, `pagamento_aprovado`, `pagamento_recusado`, `pedido_enviado`.

> **Pagamento:** a URL do Mock de Pagamento é gerada pelo MS.Pagamento *depois* do
> `pedido.estoque_ok`, então ela chega ao front-end por este fluxo SSE (status
> `pagamento_pendente` com `checkoutUrl`). O front-end então abre uma nova aba.

### 🔧 `POST /api/interesse`
Registra interesse em promoções e publica `interesse.promocao`.
```json
{ "email": "cliente@email.com", "categorias": ["A", "B"] }
```

### 🔧 `DELETE /api/interesse/{email}`
Cancela o interesse em receber e-mails de promoções.

## Estrutura

```
src/
  api/
    client.js     # wrappers REST + URL do stream SSE
    status.js     # rótulos/estilos dos status de pedido
  hooks/
    useSSE.js     # hook para consumir EventSource
  screens/
    Catalogo.jsx
    Carrinho.jsx
    MeusPedidos.jsx
    Promocoes.jsx
  App.jsx         # shell com navegação por abas
```
