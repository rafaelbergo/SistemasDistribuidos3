# Cadastro de e-mail e categorias — etapa 2

## Fluxo implementado

1. A aba Promoções envia `POST /api/interesse` com `{ "email": "cliente@example.com", "categorias": ["A", "B"] }`.
2. O Gateway valida e normaliza os dados e publica `interesse.promocao` assinado no exchange `eCommerce`.
3. O MS.Promocoes verifica produtor, assinatura e routing key, e salva o cadastro antes de confirmar o consumo.
4. Um novo cadastro do mesmo e-mail substitui suas categorias, sem duplicar o endereço.
5. `DELETE /api/interesse/{email}` publica `interesse.cancelado`; o consumidor remove a inscrição.

As duas rotas retornam **202**, pois o processamento no MS.Promocoes é assíncrono.
Validação inválida retorna **400**; falha ao publicar ou ausência de fila vinculada
retorna **503**. E-mails são normalizados para minúsculas; categorias A/B/C são
normalizadas, ordenadas e deduplicadas. O cancelamento pode ser repetido.

## Executar

Inicie RabbitMQ, MS.Promocoes e MS.Principal junto dos serviços habituais. O menu
existente de geração de promoções continua disponível, enquanto o consumidor de
interesses trabalha em um canal separado.

Para executar somente o consumidor, sem interação no console:

```powershell
dotnet run --project MS.Promocoes -- --consumer-only
```

O arquivo padrão é `MS.Promocoes/Dados/interesses.json`, ignorado pelo Git. Ele
preserva as inscrições e os IDs das mensagens já aplicadas entre reinícios. O
serviço salva por substituição do arquivo temporário antes de enviar o ACK; uma
falha de escrita mantém o estado anterior e permite nova tentativa. Mensagens
malformadas, de produtor inesperado ou com assinatura inválida são descartadas.

Configuração opcional por variáveis de ambiente:

- `Promocoes__InteressesPath`: caminho alternativo do arquivo de cadastro.
- `RabbitMq__Uri`: conexão AMQP alternativa, incluindo vhost.
- `SOLUTION_ROOT_PATH`: raiz para chaves e dados, útil em testes isolados.

O armazenamento em arquivo foi feito para **uma instância** do MS.Promocoes por
arquivo. Múltiplas instâncias precisam de armazenamento compartilhado com controle
transacional. Para a etapa 3, `InteressesStore.ListarDestinatarios(categoria)` já
retorna os e-mails inscritos na categoria.

## Testes automatizados

Na raiz do repositório, com .NET SDK 10:

```powershell
dotnet build SistemasDistribuidos2.slnx --artifacts-path artifacts/payment-tests -m:1
dotnet artifacts/payment-tests/bin/Interesses.Regression/debug/Interesses.Regression.dll
python tests/interesses_e2e.py
```

O executável de regressão não usa pacotes de teste adicionais. Verifica validação,
normalização, atualização, duplicidades, cancelamento, reinscrição, persistência,
concorrência e nova tentativa após falha de escrita.

A integração exige Python 3.10+ e o mesmo RabbitMQ de `tests/README.md`. Reutiliza
seus parâmetros `RABBITMQ_MANAGEMENT_URL`, `RABBITMQ_TEST_USER` e
`RABBITMQ_TEST_PASSWORD`. O executor cria vhost, portas, chaves e arquivos
temporários próprios e remove esses recursos ao terminar. Não usa os cadastros
nem as filas habituais.

A integração verifica o fluxo REST/RabbitMQ/arquivo, trabalho enfileirado com
consumidor desligado, retomada após reinício, rejeição de assinatura adulterada,
mensagem antiga após cancelamento e resposta 503 quando não existe fila de destino.

Para conferir pelo front-end: inscreva um e-mail nas categorias A/B, envie outra
inscrição do mesmo endereço apenas em C e confira um único registro em C no arquivo.
Cancele e confirme sua remoção. Reinicie o MS.Promocoes para verificar a persistência.

O envio de promoções por e-mail está implementado na etapa 3. Veja configuração e
testes em [MS.Promocoes/README.md](../MS.Promocoes/README.md).
