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

        // Check if Order exists
        if (!_pagamentoManager.PedidosPendentes.TryRemove(payload.PedidoId, out var pedido))
        {
            Console.WriteLine($"[MS.Pagamento] Error: Order already processed or not found");
            return NotFound("Order not found or already processed.");
        }

        // Publish event to RabbitMQ based on the payment status
        if (payload.Status.Equals("APROVADO", StringComparison.OrdinalIgnoreCase))
        {
            await _rabbitPublisher.PublishEventAsync(pedido, "pagamento.aprovado");
        }
        else
        {
            await _rabbitPublisher.PublishEventAsync(pedido, "pagamento.recusado");
        }

        return Ok("Webhook processed with success");
    }
}