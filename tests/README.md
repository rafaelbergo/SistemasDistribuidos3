# Testes de pagamento — etapa 1

Os testes de cadastro de e-mail e categorias da etapa 2 estão em [INTERESSES.md](INTERESSES.md).
Os testes de envio de promoções da etapa 3 estão em [MS.Promocoes/README.md](../MS.Promocoes/README.md).

## Regressão sem serviços externos

Requer .NET SDK 10 e Node.js. Na raiz do repositório:

```powershell
dotnet run --project tests/Pagamento.Regression
cd front-end
npm ci
npm test
npm run build
npm run lint
```

O projeto de regressão é um executável de testes sem pacotes adicionais: retorna
código diferente de zero ao falhar. Verifica aprovação, recusa, chamadas concorrentes,
decisões conflitantes, pedido desconhecido, cadastro duplicado e nova tentativa após
falha de publicação. Os testes Node verificam a reserva da aba antes da resposta
assíncrona, navegação para o checkout, fechamento por indisponibilidade/falha,
bloqueio de pop-up, fechamento manual e replay de pedido já pago.

## Integração real: REST, RabbitMQ, checkout, webhook e SSE

Requer também Python 3.10+ e RabbitMQ com o plugin de gerenciamento disponível nas
portas locais 5672 e 15672. O broker do `compose.yaml` atende a esses requisitos.

Na raiz do repositório:

```powershell
docker compose up -d rabbitmq
dotnet build SistemasDistribuidos2.slnx --artifacts-path artifacts/payment-tests -m:1
python tests/payment_e2e.py
```

Se necessário no ambiente Windows, acrescente
`-p:MSBuildEnableWorkloadResolver=false` aos comandos .NET.

O script cria um vhost RabbitMQ exclusivo, usa portas livres, gera chaves próprias e
um estoque temporário em `artifacts/payment-tests/runs/`. Executa instâncias próprias
de Estoque, Pagamento, Entrega, Principal e Mock. Encerra essas instâncias e remove o
vhost e os arquivos temporários ao terminar, inclusive em caso de falha. Não utiliza
o estoque nem as filas das instâncias habituais. Credenciais de gerenciamento
precisam permitir criar e excluir o vhost temporário.

Configuração opcional do executor:

- `RABBITMQ_MANAGEMENT_URL`: padrão `http://localhost:15672`.
- `RABBITMQ_TEST_USER` / `RABBITMQ_TEST_PASSWORD`: padrão `guest` / `guest`.
- A conexão AMQP de teste utiliza `localhost:5672`.

Cenários verificados:

1. Aprovação pelo checkout: reserva, cobrança, webhook, SSE e envio do pedido.
2. Recusa pelo checkout: SSE, cancelamento e devolução exata ao estoque.
3. Estoque insuficiente: sem baixa, sem cobrança e sem URL de checkout.
4. Webhooks repetidos e concorrentes: sem duplicar status, entrega ou devolução.
5. Decisão conflitante: HTTP 409; pedido inexistente: 404; status inválido/nulo: 400.
6. Recriação de cobrança existente preserva a decisão original.
7. SSE separado por cliente e recuperação do histórico em nova conexão.

## Conferência no navegador

Com os serviços habituais e o front-end em execução:

1. Adicione produtos, informe um e-mail e finalize o pedido. Uma aba de espera deve
   abrir no próprio clique; ela navega para o checkout após a confirmação do estoque.
2. Clique em **Pagamento Aprovado** e acompanhe **Pagamento Aprovado** e **Pedido
   Enviado** na tela Meus Pedidos.
3. Faça outro pedido, clique em **Pagamento Recusado** e confirme o status e a
   reposição do estoque ao atualizar o catálogo.
4. Com pop-ups bloqueados, finalize outro pedido e use **Abrir pagamento em nova
   aba**, em Meus Pedidos. Também é possível trocar de tela enquanto aguarda a URL:
   a conexão SSE permanece no provider global.
5. Para reproduzir indisponibilidade pelo navegador, carregue o catálogo em dois
   navegadores com um produto cuja quantidade seja 1. Compre primeiro em um e
   finalize no segundo sem atualizar o catálogo. O segundo pedido deve informar
   indisponibilidade e fechar a aba de espera, sem gerar cobrança.

## Configuração dos serviços e limites

Os endereços padrão continuam funcionando. Para ambientes de teste, os serviços
aceitam `RabbitMq__Uri`; Principal aceita `Estoque__Url`; Pagamento aceita
`Pagamento__MockUrl` e `Pagamento__WebhookUrl`; Entrega aceita `SOLUTION_ROOT_PATH`.

O webhook confirma a mesma decisão repetida com HTTP 200 e serializa decisões por
pedido. O status só é marcado após a confirmação da publicação pelo RabbitMQ; uma
falha retorna 503 e permite nova tentativa. O publisher usa mensagens persistentes
e exige que exista fila correspondente à routing key.

O controle de pagamentos e o histórico SSE continuam em memória. A proteção contra
repetições cobre a execução atual do serviço; não oferece garantia de processamento
único após reinícios ou perda de confirmação de rede. Persistência transacional e
outbox não fazem parte desta etapa.
