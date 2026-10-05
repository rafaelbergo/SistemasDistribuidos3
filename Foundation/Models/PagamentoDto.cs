namespace Foundation.Models;

public record SolicitacaoPagamentoMock(string PedidoId, decimal ValorTotal, string WebhookUrl);
public record WebhookPayload(string PedidoId, string Status);