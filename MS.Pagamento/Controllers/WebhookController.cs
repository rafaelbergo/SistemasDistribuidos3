using Foundation.Models;
using Foundation.Services;
using Microsoft.AspNetCore.Mvc;
using MS.Pagamento.Services;

namespace MS.Pagamento.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WebhookController : ControllerBase
{
    private readonly PagamentoManager _pagamentoManager;
    private readonly RabbitMqPublisher _rabbitPublisher;

    public WebhookController(PagamentoManager pagamentoManager, RabbitMqPublisher rabbitPublisher)
    {
        _pagamentoManager = pagamentoManager;
        _rabbitPublisher = rabbitPublisher;
    }

    [HttpPost]
    public async Task<IActionResult> ReceberStatusPagamento([FromBody] WebhookPayload payload)
    {
        Console.WriteLine($"[MS.Pagamento] Webhook received for OrderId: {payload.PedidoId}, status: {payload.Status}");

        string? status = payload.Status?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(payload.PedidoId) || status is not ("APROVADO" or "RECUSADO"))
            return BadRequest("PedidoId and status APROVADO or RECUSADO are required.");

        ResultadoPagamento resultado;
        try
        {
            resultado = await _pagamentoManager.ProcessarAsync(payload.PedidoId, status,
                (pedido, routingKey) => _rabbitPublisher.PublishEventAsync(pedido, routingKey));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MS.Pagamento] Failed to publish payment: {ex.Message}");
            return StatusCode(503, "Payment notification unavailable. Retry the webhook.");
        }

        return resultado switch
        {
            ResultadoPagamento.NaoEncontrado => NotFound("Order not found."),
            ResultadoPagamento.Conflito => Conflict("Order already processed with a different status."),
            ResultadoPagamento.Repetido => Ok("Webhook already processed."),
            _ => Ok("Webhook processed with success")
        };
    }
}
