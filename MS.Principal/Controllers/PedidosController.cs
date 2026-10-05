using Foundation.Models;
using Foundation.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace MS.Principal.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PedidosController : ControllerBase
{
    private readonly RabbitMqPublisher _rabbitPublisher;
    private readonly IHttpClientFactory _httpClientFactory;
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    public PedidosController(RabbitMqPublisher rabbitPublisher, IHttpClientFactory httpClientFactory)
    {
        _rabbitPublisher = rabbitPublisher;
        _httpClientFactory = httpClientFactory;
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