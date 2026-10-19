using System.Collections.Concurrent;
using System.Globalization;
using System.Net;

// Mock de Pagamento: external system that simulates a payment provider.
// 1. MS.Pagamento POSTs /api/cobranca and receives the checkout URL.
// 2. The user opens that URL in a new browser tab and clicks Aprovar/Recusar.
// 3. The mock POSTs the result (APROVADO/RECUSADO) to the webhook URL that
//    MS.Pagamento sent when creating the charge.

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();

var app = builder.Build();

var cobrancas = new ConcurrentDictionary<string, Cobranca>();

// Create a charge (called by MS.Pagamento via REST)
app.MapPost("/api/cobranca", (SolicitacaoCobranca request, HttpRequest http) =>
{
    if (string.IsNullOrWhiteSpace(request.PedidoId) || string.IsNullOrWhiteSpace(request.WebhookUrl))
        return Results.BadRequest("PedidoId and WebhookUrl are required");

    var cobranca = new Cobranca(request.PedidoId, request.ValorTotal, request.WebhookUrl);
    cobranca = cobrancas.GetOrAdd(request.PedidoId, cobranca);
    if (cobranca.ValorTotal != request.ValorTotal || cobranca.WebhookUrl != request.WebhookUrl)
        return Results.Conflict("A charge with different data already exists for this order.");

    string checkoutUrl = $"{http.Scheme}://{http.Host}/checkout/{Uri.EscapeDataString(request.PedidoId)}";
    Console.WriteLine($"[Mock.Pagamento] Charge created for Order {request.PedidoId}: {checkoutUrl}");

    return Results.Created(checkoutUrl, new CobrancaCriada(request.PedidoId, checkoutUrl));
});

// Checkout page opened by the user in a new tab
app.MapGet("/checkout/{pedidoId}", (string pedidoId) =>
{
    if (!cobrancas.TryGetValue(pedidoId, out var cobranca))
        return Pagina("Cobrança não encontrada", "<p>Nenhuma cobrança foi criada para este pedido.</p>", 404);

    if (cobranca.Status != null)
        return PaginaResultado(cobranca);

    string id = WebUtility.HtmlEncode(cobranca.PedidoId);
    string acao = $"/checkout/{Uri.EscapeDataString(cobranca.PedidoId)}";

    return Pagina("Pagamento do pedido", $"""
        <p class="label">Pedido</p>
        <p class="mono">{id}</p>
        <p class="label">Valor</p>
        <p class="valor">{FormatarValor(cobranca.ValorTotal)}</p>
        <div class="acoes">
          <form method="post" action="{acao}/aprovar"><button class="aprovar">Pagamento Aprovado</button></form>
          <form method="post" action="{acao}/recusar"><button class="recusar">Pagamento Recusado</button></form>
        </div>
        """);
});

// User decision: notify MS.Pagamento through the webhook
app.MapPost("/checkout/{pedidoId}/{decisao}", async (string pedidoId, string decisao, IHttpClientFactory httpClientFactory) =>
{
    if (!cobrancas.TryGetValue(pedidoId, out var cobranca))
        return Pagina("Cobrança não encontrada", "<p>Nenhuma cobrança foi criada para este pedido.</p>", 404);

    string? status = decisao switch
    {
        "aprovar" => "APROVADO",
        "recusar" => "RECUSADO",
        _ => null
    };
    if (status == null)
        return Pagina("Ação inválida", "<p>Use aprovar ou recusar.</p>", 400);

    await cobranca.Lock.WaitAsync();
    try
    {
        if (cobranca.Status != null)
            return PaginaResultado(cobranca);

        try
        {
            var client = httpClientFactory.CreateClient();
            var response = await client.PostAsJsonAsync(cobranca.WebhookUrl, new WebhookPayload(cobranca.PedidoId, status));
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[Mock.Pagamento] Webhook for Order {pedidoId} failed, status: {response.StatusCode}");
                return PaginaErroWebhook(cobranca, $"O MS Pagamento respondeu {(int)response.StatusCode}.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Mock.Pagamento] Error on call webhook for Order {pedidoId}: {ex.Message}");
            return PaginaErroWebhook(cobranca, "Não foi possível contatar o MS Pagamento.");
        }

        cobranca.Status = status;
        Console.WriteLine($"[Mock.Pagamento] Webhook sent for Order {pedidoId}: {status}");
        return PaginaResultado(cobranca);
    }
    finally
    {
        cobranca.Lock.Release();
    }
});

app.Run();

static string FormatarValor(decimal valor) =>
    "R$ " + valor.ToString("N2", CultureInfo.InvariantCulture).Replace(",", "_").Replace(".", ",").Replace("_", ".");

static IResult PaginaResultado(Cobranca cobranca)
{
    bool aprovado = cobranca.Status == "APROVADO";
    return Pagina(aprovado ? "Pagamento aprovado" : "Pagamento recusado", $"""
        <p class="resultado {(aprovado ? "ok" : "erro")}">{(aprovado ? "✔ Pagamento aprovado" : "✘ Pagamento recusado")}</p>
        <p>Pedido <span class="mono">{WebUtility.HtmlEncode(cobranca.PedidoId)}</span> · {FormatarValor(cobranca.ValorTotal)}</p>
        <p class="dica">O status foi enviado à loja. Você já pode fechar esta aba.</p>
        """);
}

static IResult PaginaErroWebhook(Cobranca cobranca, string motivo) =>
    Pagina("Falha ao notificar a loja", $"""
        <p class="resultado erro">{WebUtility.HtmlEncode(motivo)}</p>
        <p class="dica">Verifique se o MS Pagamento está em execução e tente novamente.</p>
        <p><a href="/checkout/{Uri.EscapeDataString(cobranca.PedidoId)}">Voltar ao pagamento</a></p>
        """, 502);

static IResult Pagina(string titulo, string corpo, int statusCode = 200) =>
    Results.Content($$"""
        <!doctype html>
        <html lang="pt-BR">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Mock de Pagamento</title>
          <style>
            body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: #f1f5f9; font-family: system-ui, sans-serif; color: #0f172a; }
            main { width: min(420px, calc(100% - 32px)); background: #fff; border-radius: 12px; padding: 28px; box-shadow: 0 4px 20px rgb(0 0 0 / .08); }
            .marca { margin: 0 0 4px; font-size: 12px; letter-spacing: .08em; text-transform: uppercase; color: #64748b; }
            h1 { margin: 0 0 20px; font-size: 20px; }
            .label { margin: 12px 0 2px; font-size: 13px; color: #64748b; }
            .mono { margin: 0; font-family: ui-monospace, monospace; font-size: 13px; word-break: break-all; }
            .valor { margin: 0; font-size: 28px; font-weight: 700; color: #1d4ed8; }
            .acoes { display: grid; gap: 10px; margin-top: 24px; }
            form { margin: 0; }
            button { width: 100%; padding: 12px; border: 0; border-radius: 8px; font-size: 15px; font-weight: 600; color: #fff; cursor: pointer; }
            .aprovar { background: #16a34a; } .aprovar:hover { background: #15803d; }
            .recusar { background: #dc2626; } .recusar:hover { background: #b91c1c; }
            .resultado { font-size: 18px; font-weight: 600; }
            .ok { color: #16a34a; } .erro { color: #dc2626; }
            .dica { font-size: 13px; color: #64748b; }
          </style>
        </head>
        <body>
          <main>
            <p class="marca">Mock de Pagamento</p>
            <h1>{{WebUtility.HtmlEncode(titulo)}}</h1>
            {{corpo}}
          </main>
        </body>
        </html>
        """, "text/html; charset=utf-8", statusCode: statusCode);

record SolicitacaoCobranca(string PedidoId, decimal ValorTotal, string WebhookUrl);
record CobrancaCriada(string PedidoId, string CheckoutUrl);
record WebhookPayload(string PedidoId, string Status);

class Cobranca(string pedidoId, decimal valorTotal, string webhookUrl)
{
    public string PedidoId { get; } = pedidoId;
    public decimal ValorTotal { get; } = valorTotal;
    public string WebhookUrl { get; } = webhookUrl;
    public string? Status { get; set; }
    public SemaphoreSlim Lock { get; } = new(1, 1);
}
