using Foundation.Models;
using MS.Promocoes.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

string scratchRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "scratch"));
string directory = Path.Combine(scratchRoot, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var store = new InteressesStore(Path.Combine(directory, "interesses.json"));
    store.Aplicar(new() { Email = "a@example.com", Categorias = ["A"] }, "a");
    store.Aplicar(new() { Email = "b@example.com", Categorias = ["B"] }, "b");
    store.Aplicar(new() { Email = "cancelado@example.com", Categorias = ["A"] }, "c1");
    store.Aplicar(new() { Email = "cancelado@example.com", Cancelado = true }, "c2");
    var sentPath = Path.Combine(directory, "envios.json");
    var envios = new EmailEnviosStore(sentPath);
    var options = new EmailOptions("test-key", "Loja <loja@example.com>", new Uri("http://localhost/emails"), 0);
    var handler = new FakeHandler();
    using var client = new HttpClient(handler);
    var sender = new ResendEmailSender(client, options);
    var dispatcher = new PromocaoEmailDispatcher(store, envios, sender);
    var promotion = new Promocao { Id = "one", Categoria = "A", Produto = "<Produto>\r\nOferta", Descricao = "<script>test</script>", DescontoPercentual = 25 };

    Check(await dispatcher.EnviarAsync(promotion), "Expected successful dispatch");
    Check(handler.Calls.Count == 1 && handler.Calls[0].Email == "a@example.com", "Category/cancellation filter failed");
    Check(handler.Calls[0].Html.Contains("&lt;script&gt;") && !handler.Calls[0].Html.Contains("<script>"), "HTML must escape promotion content");
    Check(!handler.Calls[0].Subject.Contains('\n'), "Subject must not contain line breaks");
    Check(handler.Calls[0].Auth == "Bearer test-key", "Missing authorization");
    var restarted = new PromocaoEmailDispatcher(store, new EmailEnviosStore(sentPath), sender);
    Check(await restarted.EnviarAsync(promotion) && handler.Calls.Count == 1, "Replay/restart must not resend");
    Console.WriteLine("PASS: category filtering, cancellations, payload, auth, HTML escaping and durable duplicate protection");

    handler.Response = count => count < 3 ? (count == 1 ? 429 : 503) : 200;
    handler.Attempt = 0;
    promotion.Id = "retry";
    int before = handler.Calls.Count;
    Check(await dispatcher.EnviarAsync(promotion), "Transient errors should recover");
    Check(handler.Calls.Count - before == 3, "Expected exactly three HTTP attempts");
    Check(handler.Calls.Skip(before).Select(c => c.Key).Distinct().Count() == 1, "Retries must reuse the idempotency key");
    Console.WriteLine("PASS: 429/503 retries with stable idempotency key");

    store.Aplicar(new() { Email = "z@example.com", Categorias = ["A"] }, "z");
    handler.Response = _ => 200;
    handler.FailEmail = "a@example.com";
    promotion.Id = "partial";
    Check(!await dispatcher.EnviarAsync(promotion), "Recipient failure must be reported");
    Check(envios.JaEnviado(EmailEnviosStore.Chave(promotion.Id, "z@example.com")), "Failure must not stop the next recipient");
    before = handler.Calls.Count;
    handler.FailEmail = null;
    Check(await dispatcher.EnviarAsync(promotion) && handler.Calls.Count == before + 1, "Retry must target only the failed recipient");
    Console.WriteLine("PASS: permanent failure isolation and partial retry without duplicate successful sends");

    handler.BeforeResponse = () => store.Aplicar(new() { Email = "a@example.com", Cancelado = true }, "cancel-during-retry");
    handler.Response = _ => 429;
    before = handler.Calls.Count;
    var result = await sender.EnviarAsync(promotion, "a@example.com", "cancel-key", () => store.ListarDestinatarios("A").Contains("a@example.com"));
    Check(result.Cancelado && handler.Calls.Count == before + 1, "Cancellation must stop a pending retry");
    var missing = await new ResendEmailSender(client, options with { ApiKey = "" })
        .EnviarAsync(promotion, "a@example.com", "missing", () => true);
    Check(!missing.Sucesso && handler.Calls.Count == before + 1, "Missing configuration must not call the API");
    Console.WriteLine("PASS: cancellation during retry and missing credentials");

    handler.BeforeResponse = null;
    handler.Response = _ => 200;
    handler.Payloads.Enqueue("[]");
    handler.Payloads.Enqueue("{}");
    before = handler.Calls.Count;
    result = await sender.EnviarAsync(promotion, "a@example.com", "invalid-response", () => true);
    Check(result.Sucesso && handler.Calls.Count == before + 3, "Unexpected success response must be retried, not marked as sent");
    Console.WriteLine("PASS: malformed success payload does not acknowledge an unsent email");
}
finally
{
    if (Path.GetDirectoryName(Path.GetFullPath(directory)) != scratchRoot)
        throw new InvalidOperationException("Refusing cleanup outside scratch directory.");
    Directory.Delete(directory, recursive: true);
}

record EmailCall(string Email, string Key, string Auth, string Html, string Subject);

sealed class FakeHandler : HttpMessageHandler
{
    public List<EmailCall> Calls { get; } = [];
    public Func<int, int> Response { get; set; } = _ => 200;
    public int Attempt { get; set; }
    public string? FailEmail { get; set; }
    public Action? BeforeResponse { get; set; }
    public Queue<string> Payloads { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        string email = body.RootElement.GetProperty("to")[0].GetString()!;
        Calls.Add(new(email, request.Headers.GetValues("Idempotency-Key").Single(), request.Headers.Authorization!.ToString(),
            body.RootElement.GetProperty("html").GetString()!, body.RootElement.GetProperty("subject").GetString()!));
        BeforeResponse?.Invoke();
        int status = email == FailEmail ? 422 : Response(++Attempt);
        string payload = status == 200 ? (Payloads.Count > 0 ? Payloads.Dequeue() : "{\"id\":\"test-id\"}") : "{}";
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(payload) };
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
        return response;
    }
}
