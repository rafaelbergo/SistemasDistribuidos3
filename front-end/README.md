# Front-end — e-Commerce (Trab2 Sistemas Distribuídos)

Interface web (React + Vite + Tailwind CSS) que consome o **API Gateway (MS.Principal)**
via **REST** e **SSE**. Corresponde ao componente *Frontend web* do Trab2.

## Como rodar o front-end

```bash
npm install
npm run dev      # servidor de desenvolvimento (Vite), em http://localhost:5173
npm run build    # build de produção em dist/
npm run lint     # oxlint
```

Em dev, o Vite faz **proxy de `/api`** para o gateway (same-origin, sem CORS).
Veja `.env` / `.env.example`:

```
VITE_API_URL=              # vazio = usa o proxy do Vite (recomendado em dev)
# VITE_API_URL=http://localhost:5059   # aponta direto no gateway (exige CORS no backend)
# VITE_GATEWAY_URL=http://localhost:5059   # alvo do proxy (padrão)
```

## Rodando o sistema completo (para testar ao vivo)

1. **RabbitMQ:** `docker compose up -d rabbitmq` (na raiz do projeto).
2. **MS.Estoque** (precisa expor a porta HTTPS 7072, consumida pelo gateway):
   ```bash
   dotnet run --project MS.Estoque --launch-profile https
   ```
3. **MS.Principal** (gateway). A chamada gateway→estoque é HTTPS; é preciso
   confiar no dev-cert e informar o `SSL_CERT_DIR` para o `HttpClient`:
   ```bash
   dotnet dev-certs https --trust
   export SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:/etc/ssl/certs"
   dotnet run --project MS.Principal --launch-profile http
   ```
   Sem isso, o gateway responde `500 Error on connect to MS.Estoque` (TLS).
4. **Front-end:** `npm run dev` e abra http://localhost:5173.

> Demais serviços (MS.Pagamento, MS.Entrega, MS.Promocoes, Consumidor.C1/C2)
> são iniciados do mesmo modo com `dotnet run --project <serviço>`.

## Funcionalidades (requisitos do PDF)

| Tela          | Requisito                                   | Status |
|---------------|---------------------------------------------|--------|
| Catálogo      | Consultar produtos disponíveis (REST)       | ✅ |
| Carrinho      | Criar pedido (REST) + abrir aba de pagamento| ✅ |
| Meus Pedidos  | Acompanhar status em tempo real (SSE)       | ✅ (depende do endpoint SSE no backend) |
| Promoções     | Inscrever/cancelar e-mail por categorias    | ✅ (depende dos endpoints de interesse no backend) |

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
