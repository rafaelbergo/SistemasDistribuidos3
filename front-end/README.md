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

Atalho (na raiz do projeto): com o RabbitMQ no ar, `./start-gateway.sh` abre um
terminal para cada serviço (inclusive o Mock de Pagamento) já com os ajustes abaixo;
`./start-gateway.sh --stop` encerra todos. Passo a passo manual:

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
4. **Pagamento:** `dotnet run --project Mock.Pagamento` (http://localhost:5300) e
   `dotnet run --project MS.Pagamento` (http://localhost:5207, recebe o webhook).
5. **MS.Entrega:** `dotnet run --project MS.Entrega` (publica `pedido.enviado`).
6. **Front-end:** `npm run dev` e abra http://localhost:5173.

> Demais serviços (MS.Promocoes, Consumidor.C1/C2) são iniciados do mesmo modo
> com `dotnet run --project <serviço>`.

## Funcionalidades (requisitos do PDF)

| Tela          | Requisito                                   | Status |
|---------------|---------------------------------------------|--------|
| Catálogo      | Consultar produtos disponíveis (REST)       | ✅ |
| Carrinho      | Criar pedido (REST) + abrir aba de pagamento| ✅ |
| Meus Pedidos  | Acompanhar status em tempo real (SSE)       | ✅ |
| Promoções     | Inscrever, atualizar e cancelar e-mail por categorias | ✅ (Gateway e consumidor implementados) |

## Contrato da API (Gateway / MS.Principal)

O front-end utiliza o contrato abaixo. O cadastro de interesses e o envio de
promoções por e-mail estão implementados. Configure a API de e-mail no MS.Promocoes.

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

### ✅ `GET /api/pedidos/eventos?clienteId=...`  (SSE — `text/event-stream`)
Fluxo de notificações em tempo real do(s) pedido(s) do cliente. O gateway consome
(fila `fila_principal`) cada mudança de estado — `pedido.estoque_ok`,
`estoque.indisponivel`, `pagamento.pendente`, `pagamento.aprovado`,
`pagamento.recusado`, `pedido.enviado` — verifica a assinatura e a encaminha às
conexões SSE abertas do cliente dono do pedido.

Cada evento SSE tem um `id` sequencial e `data` em JSON:
```jsonc
{
  "pedidoId": "ab12…",
  "status": "pagamento_pendente",   // ver valores abaixo
  "mensagem": "Aguardando pagamento",
  "checkoutUrl": "http://localhost:5300/checkout/ab12…", // só em pagamento_pendente
  "timestamp": "2026-10-12T14:30:00Z"
}
```
Valores de `status` (`src/api/status.js`): `estoque_confirmado`,
`estoque_indisponivel`, `pagamento_pendente`, `pagamento_aprovado`,
`pagamento_recusado`, `pedido_enviado`.

Ao conectar, o cliente recebe o histórico dos seus pedidos; ao reconectar, o
`EventSource` envia `Last-Event-ID` e só os eventos posteriores são reenviados.
O gateway manda um heartbeat (`: ping`) a cada 15 s.

> **Pagamento:** depois do `pedido.estoque_ok`, o MS.Pagamento cria a cobrança no
> Mock de Pagamento e publica `pagamento.pendente` com o `checkoutUrl`; o gateway o
> repassa por este fluxo SSE e o front-end oferece abrir a página em nova aba.

### Mock de Pagamento (`Mock.Pagamento`, http://localhost:5300)
Sistema externo que simula o provedor de pagamentos:
- `POST /api/cobranca` — chamado pelo MS.Pagamento com `{ pedidoId, valorTotal, webhookUrl }`;
  responde `{ pedidoId, checkoutUrl }`.
- `GET /checkout/{pedidoId}` — página aberta na nova aba, com os botões
  **Pagamento Aprovado** / **Pagamento Recusado**.
- Ao clicar, faz `POST` no `webhookUrl` (`http://localhost:5207/api/webhook` do
  MS.Pagamento) com `{ pedidoId, status: "APROVADO" | "RECUSADO" }`.

### `POST /api/interesse`
Registra ou substitui as categorias de interesse de um e-mail e publica
`interesse.promocao`, com assinatura digital. Responde HTTP 202 com e-mail e
categorias normalizados; o MS.Promocoes processa e persiste o cadastro de forma
assíncrona. Categorias permitidas: A, B e C. Dados inválidos retornam 400;
falha no encaminhamento retorna 503.
```json
{ "email": "cliente@email.com", "categorias": ["A", "B"] }
```

### `DELETE /api/interesse/{email}`
Publica `interesse.cancelado` e responde HTTP 202. O MS.Promocoes remove a
inscrição; repetir o cancelamento é permitido. O e-mail deve estar codificado
no caminho da URL, como já faz o cliente REST.

O MS.Promocoes precisa estar iniciado ao menos uma vez para declarar a fila de
interesses. Os cadastros ficam em `MS.Promocoes/Dados/interesses.json`, fora do Git.
Consulte `../tests/INTERESSES.md` para testar e executar o consumidor sem menu.
O disparo por API externa de e-mail está implementado; veja configuração e testes
em `../MS.Promocoes/README.md`.

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
