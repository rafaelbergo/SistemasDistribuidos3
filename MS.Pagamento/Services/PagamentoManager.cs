using Foundation.Models;
using System.Collections.Concurrent;

namespace MS.Pagamento.Services;

public class PagamentoManager
{
    private readonly ConcurrentDictionary<string, Pagamento> _pagamentos = new();

    public bool Registrar(PedidoCriado pedido) => _pagamentos.TryAdd(pedido.Id, new Pagamento(pedido));

    public async Task<ResultadoPagamento> ProcessarAsync(string pedidoId, string status,
        Func<PedidoCriado, string, Task> publicar)
    {
        if (!_pagamentos.TryGetValue(pedidoId, out var pagamento))
            return ResultadoPagamento.NaoEncontrado;

        await pagamento.Lock.WaitAsync();
        try
        {
            if (pagamento.Status != null)
                return pagamento.Status == status ? ResultadoPagamento.Repetido : ResultadoPagamento.Conflito;

            // Keep the order pending if publishing fails, so the webhook can be retried.
            await publicar(pagamento.Pedido, status == "APROVADO" ? "pagamento.aprovado" : "pagamento.recusado");
            pagamento.Status = status;
            return ResultadoPagamento.Processado;
        }
        finally
        {
            pagamento.Lock.Release();
        }
    }

    private sealed class Pagamento(PedidoCriado pedido)
    {
        public PedidoCriado Pedido { get; } = pedido;
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public string? Status { get; set; }
    }
}

public enum ResultadoPagamento { Processado, Repetido, Conflito, NaoEncontrado }
