# Promoções por e-mail — etapa 3

O serviço consome os cadastros da etapa 2 e envia promoções pela API REST da Resend.
O menu continua publicando mensagens assinadas no exchange `Promoções`. Uma fila
exclusiva recebe essas mensagens e envia e-mails somente aos inscritos na categoria.

## Configurar e executar

Preencha `MS.Promocoes/appsettings.json`, que é um arquivo local ignorado pelo Git:

```json
{
  "Email": {
    "ApiKey": "SUA_CHAVE_RESEND",
    "From": "Minha Loja <promocoes@seu-dominio.com>",
    "Endpoint": "https://api.resend.com/emails"
  }
}
```

O arquivo é copiado para a saída ao compilar. Depois de editar, inicie novamente
com `dotnet run --project MS.Promocoes`. Se o arquivo não existir, o serviço ainda
aceita configuração por variáveis de ambiente. Elas têm prioridade sobre o JSON
e não são expostas ao front-end. Exemplo no PowerShell:

```powershell
$env:Email__ApiKey = 'SUA_CHAVE_RESEND'
$env:Email__From = 'Minha Loja <promocoes@seu-dominio.com>'
dotnet run --project MS.Promocoes
```

`RESEND_API_KEY` também é aceito quando `Email__ApiKey` não está definido. O endpoint
padrão é `https://api.resend.com/emails`; `Email__Endpoint` permite usar uma API
simulada. HTTP só é aceito para loopback; endpoints externos exigem HTTPS.
Não há leitura automática de arquivos `.env` neste aplicativo console.

O remetente deve seguir as regras de domínio da sua conta. O remetente de teste da
Resend tem limitações de destinatários; para uma demonstração com inscritos reais,
use um domínio verificado. Referências oficiais:
[enviar e-mail](https://resend.com/docs/api-reference/emails/send-email) e
[domínios](https://resend.com/docs/dashboard/domains/introduction).

Com RabbitMQ, Gateway e front-end em execução:

1. Cadastre seu e-mail e categorias na aba **Promoções**.
2. Aguarde o processamento do cadastro no MS.Promocoes.
3. No menu do serviço, use **4 — Gerar promoção de uma categoria específica** e
   informe A, B ou C. As opções de promoção aleatória e lote continuam disponíveis.
4. O serviço envia produto, categoria, desconto e descrição em texto e HTML.
5. Cancele a inscrição pelo front-end; após o processamento, novas promoções não
   serão enviadas para aquele cadastro.

`--consumer-only` inicia os consumidores de interesses e de e-mail sem abrir o menu.
Sem chave ou remetente, o serviço declara a fila de e-mail mas não a consome: as
promoções ficam guardadas até reiniciar o serviço com a configuração preenchida.

## Falhas e repetições

- `fila_email_promocoes`: fila durável, com ACK somente após processar a promoção.
- `fila_email_falhas`: promoções com falha definitiva ou tentativas esgotadas,
  incluindo payloads inválidos ou assinaturas adulteradas.
- Cada destinatário recebe uma chamada separada. Falha em um endereço não impede
  o processamento dos seguintes.
- Timeout, falha de conexão, resposta inválida, HTTP 429 e erros HTTP 5xx recebem até
  três tentativas por processamento. O cliente respeita `Retry-After`; na ausência
  dele, usa espera progressiva. Outras respostas HTTP de erro vão para a fila de
  falhas após processar os demais destinatários.
- O estado da inscrição é conferido antes de cada tentativa. Um cancelamento não
  pode retirar uma mensagem que já foi aceita pela API.
- Uma chave estável por promoção e destinatário é enviada em `Idempotency-Key`.
  Os envios aceitos ficam em `MS.Promocoes/Dados/envios.json`, fora do Git, e não
  são repetidos após reiniciar o serviço. Use `Promocoes__EnviosPath` para mudar
  o caminho. O arquivo é administrado por uma instância do serviço.

Para reprocessar uma falha, corrija o motivo e republique **a mesma mensagem JSON
original**, preservando `Content.Id` e a assinatura, no exchange `Promoções` com a
routing key `promocao.categoria.A`, `.B` ou `.C` correspondente. Não altere o conteúdo
nem gere outro ID: os destinatários já aceitos são ignorados; os demais são
resolvidos pelo cadastro atual. A fila de falhas pode ser inspecionada pelo painel
RabbitMQ. Retire a mensagem da fila de falhas após encaminhá-la para reprocessamento.

O histórico local registra aceitação pela API, não confirmação de entrega na caixa
postal. Webhooks de entrega/bounce não estão no escopo do trabalho. Se a API aceitar
e o serviço falhar antes de salvar o histórico, a idempotência do provedor protege
novas tentativas por 24 horas. Essa janela é documentada pela
[Resend](https://resend.com/docs/dashboard/emails/idempotency-keys).

## Testes sem enviar e-mails reais

Na raiz, com .NET SDK 10, Python 3.10+ e RabbitMQ local:

```powershell
dotnet build SistemasDistribuidos2.slnx --artifacts-path artifacts/payment-tests -m:1
dotnet artifacts/payment-tests/bin/Email.Regression/debug/Email.Regression.dll
python tests/email_e2e.py
```

Se necessário no Windows, acrescente `-p:MSBuildEnableWorkloadResolver=false` à
compilação. Os parâmetros do RabbitMQ são os mesmos de `tests/README.md`.

Os testes de regressão usam um `HttpMessageHandler` simulado. A integração inicia
uma API HTTP local com o contrato da Resend, Gateway e MS.Promocoes com vhost,
portas, chaves e arquivos temporários. Ela gera promoções reais pelo menu do
serviço, verifica as assinaturas e chamadas de e-mail, respeita cancelamentos,
simula falhas 422/429/503 e confere repetição e reinício. Nenhuma chave real ou
chamada à Resend é usada; os recursos temporários são removidos ao terminar.
