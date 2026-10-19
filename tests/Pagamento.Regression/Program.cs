using Foundation.Models;
using MS.Pagamento.Services;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

foreach (string status in new[] { "APROVADO", "RECUSADO" })
{
    var manager = new PagamentoManager();
    var pedido = new PedidoCriado { Id = "pedido", ValorTotal = 20 };
    Check(manager.Registrar(pedido), "First registration must succeed");
    Check(!manager.Registrar(pedido), "Duplicate order must not recreate payment");
    int publications = 0;
    async Task Publish(PedidoCriado original, string routingKey)
    {
        Check(ReferenceEquals(original, pedido), "Original order must be preserved");
        Check(routingKey == (status == "APROVADO" ? "pagamento.aprovado" : "pagamento.recusado"), "Wrong routing key");
        Interlocked.Increment(ref publications);
        await Task.Delay(10);
    }

    var results = await Task.WhenAll(Enumerable.Range(0, 20)
        .Select(_ => manager.ProcessarAsync(pedido.Id, status, Publish)));
    Check(publications == 1, "Concurrent webhooks must publish exactly once");
    Check(results.Count(r => r == ResultadoPagamento.Processado) == 1, "Only one webhook processes payment");
    Check(results.Count(r => r == ResultadoPagamento.Repetido) == 19, "Repeated webhook must succeed idempotently");
    Check(await manager.ProcessarAsync(pedido.Id, status == "APROVADO" ? "RECUSADO" : "APROVADO", Publish)
        == ResultadoPagamento.Conflito, "Conflicting decision must be rejected");
    Check(await manager.ProcessarAsync("unknown", status, Publish) == ResultadoPagamento.NaoEncontrado, "Unknown order must be rejected");
    Console.WriteLine($"PASS: {status}, 20 concurrent callbacks, conflict and unknown order");
}

var retryManager = new PagamentoManager();
retryManager.Registrar(new PedidoCriado { Id = "retry" });
try
{
    await retryManager.ProcessarAsync("retry", "APROVADO", (_, _) => throw new IOException("Broker unavailable"));
    throw new Exception("Publishing failure must propagate");
}
catch (IOException) { }
Check(await retryManager.ProcessarAsync("retry", "APROVADO", (_, _) => Task.CompletedTask)
    == ResultadoPagamento.Processado, "Failed publication must leave the order available for retry");
Console.WriteLine("PASS: retry after publishing failure");

var raceManager = new PagamentoManager();
raceManager.Registrar(new PedidoCriado { Id = "race" });
var decisions = await Task.WhenAll(new[] { "APROVADO", "RECUSADO" }.Select(status =>
    raceManager.ProcessarAsync("race", status, async (_, _) => await Task.Delay(10))));
Check(decisions.Count(r => r == ResultadoPagamento.Processado) == 1 &&
      decisions.Count(r => r == ResultadoPagamento.Conflito) == 1,
      "Concurrent opposing decisions must have exactly one winner");
Console.WriteLine("PASS: concurrent approval and refusal");
