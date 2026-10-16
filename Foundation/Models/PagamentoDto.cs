namespace Foundation.Models;

public record SolicitacaoPagamentoMock(string PedidoId, decimal ValorTotal, string WebhookUrl);
public record WebhookPayload(string PedidoId, string Status);
public record CobrancaCriada(string PedidoId, string CheckoutUrl);

// Published as pagamento.pendente once the Mock generates the checkout URL,
// so the gateway can forward it to the client over SSE.
public class PagamentoPendente
{
    public string PedidoId { get; set; } = string.Empty;
    public string ClienteId { get; set; } = string.Empty;
    public decimal ValorTotal { get; set; } = 0;
    public string CheckoutUrl { get; set; } = string.Empty;
}