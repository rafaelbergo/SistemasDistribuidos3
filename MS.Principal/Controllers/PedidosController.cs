using Foundation.Models;
using Foundation.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using MS.Principal.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MS.Principal.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PedidosController : ControllerBase
{
    private readonly RabbitMqPublisher _rabbitPublisher;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PedidoEventosHub _eventosHub;
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions _sseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(15);

    public PedidosController(RabbitMqPublisher rabbitPublisher, IHttpClientFactory httpClientFactory, PedidoEventosHub eventosHub)
    {
        _rabbitPublisher = rabbitPublisher;
        _httpClientFactory = httpClientFactory;
        _eventosHub = eventosHub;
    }

    // SSE stream with the status changes of the client's orders
    [HttpGet("eventos")]
    public async Task StreamEventos([FromQuery] string? clienteId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clienteId))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsync("clienteId is required", ct);
            return;
        }

        // On reconnect the browser sends the last id it received, so only newer events are replayed
        long.TryParse(Request.Headers["Last-Event-ID"], out long lastEventId);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        using var assinatura = _eventosHub.Assinar(clienteId.Trim(), lastEventId);
        Console.WriteLine($"[MS.Principal] SSE client connected: {clienteId}");

        try
        {
            await Response.WriteAsync("retry: 3000\n\n", ct);

            foreach (var item in assinatura.Pendentes)
                await EnviarEventoAsync(item, ct);
            await Response.Body.FlushAsync(ct);

            Task<bool>? espera = null;
            while (!ct.IsCancellationRequested)
            {
                espera ??= assinatura.Novos.WaitToReadAsync(ct).AsTask();
                var concluida = await Task.WhenAny(espera, Task.Delay(_heartbeatInterval, ct));

                if (concluida != espera)
                {
                    // Comment line keeps the connection alive through proxies
                    await Response.WriteAsync(": ping\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                    continue;
                }

                if (!await espera)
                    break;
                espera = null;

                while (assinatura.Novos.TryRead(out var item))
                    await EnviarEventoAsync(item, ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client closed the connection
        }

        Console.WriteLine($"[MS.Principal] SSE client disconnected: {clienteId}");
    }

    private Task EnviarEventoAsync(PedidoEventoSse item, CancellationToken ct)
    {
        string data = JsonSerializer.Serialize(item.Evento, _sseJsonOptions);
        return Response.WriteAsync($"id: {item.Id}\ndata: {data}\n\n", ct);
    }

    [HttpPost]
    public async Task<IActionResult> CriarPedido([FromBody] PedidoRequest request)
    {
        if (request.Itens == null || !request.Itens.Any())
        {
            return BadRequest("Order must contain at least one item");
        }

        var client = _httpClientFactory.CreateClient("EstoqueClient");
        decimal totalValue = 0;
        var itensPedido = new List<ItemPedido>();

        // Check each item
        foreach (var reqItem in request.Itens)
        {
            var response = await client.GetAsync($"/api/produtos/{reqItem.Id}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)        
                return BadRequest($"Product {reqItem.Id} not found");
            
            if (!response.IsSuccessStatusCode)         
                return StatusCode(500, "Error on validate product, invalid response");

            var productJson = await response.Content.ReadAsStringAsync();
            var product = JsonSerializer.Deserialize<ProdutoCatalogoDto>(productJson, _jsonOptions);
            if (product == null)
                return StatusCode(500, $"Error on get product {reqItem.Id}");

            totalValue += product.Preco * reqItem.Quantidade;

            itensPedido.Add(new ItemPedido
            {
                Id = reqItem.Id,
                Quantidade = reqItem.Quantidade,
                Preco = product.Preco
            });
        }

        string pedidoId = Guid.NewGuid().ToString("N");

        var pedido = new PedidoCriado
        {
            Id = pedidoId,
            ClienteId = request.ClienteId,
            Itens = itensPedido,
            ValorTotal = totalValue
        };

        try
        {
            await _rabbitPublisher.PublishEventAsync(pedido, "pedido.criado");

            return Accepted(new
            {
                mensagem = "Order created and sent to processing",
                pedidoId = pedidoId,
                totalValue = totalValue
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MS.Principal] Error on publish event: {ex.Message}");
            return StatusCode(500, $"Error on publish new order event: {ex.Message}");
        }
    }
}