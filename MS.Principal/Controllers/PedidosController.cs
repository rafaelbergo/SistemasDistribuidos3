using Foundation.Models;
using Microsoft.AspNetCore.Mvc;
using MS.Pagamento.Services;

namespace MS.Principal.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PedidosController : ControllerBase
{
    private readonly RabbitMqPublisher _rabbitPublisher;

    public PedidosController(RabbitMqPublisher rabbitPublisher)
    {
        _rabbitPublisher = rabbitPublisher;
    }

    [HttpPost]
    public async Task<IActionResult> CriarPedido([FromBody] PedidoRequest request)
    {
        if (request.Itens == null || !request.Itens.Any())
        {
            return BadRequest("Order must have at least one item");
        }

        // Create random OrderId
        string pedidoId = Guid.NewGuid().ToString("N");

        // Create list of items based on request items
        var itensPedido = request.Itens.Select(i => new ItemPedido
        {
            Id = i.Id,
            Quantidade = i.Quantidade
        }).ToList();

        decimal valorTotalCalculado = itensPedido.Sum(i => i.Quantidade * 1.0m);

        var pedido = new PedidoCriado
        {
            Id = pedidoId,
            ClienteId = request.ClienteId,
            Itens = itensPedido,
            ValorTotal = valorTotalCalculado
        };

        try
        {
            await _rabbitPublisher.PublishEventAsync(pedido, "pedido.criado");

            return Accepted(new { mensagem = "Order received, processing started.", id = pedidoId });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MS.Principal] Error on publish order: {ex.Message}");
            return StatusCode(500, "Error on publish order: " + ex.Message);
        }
    }
}