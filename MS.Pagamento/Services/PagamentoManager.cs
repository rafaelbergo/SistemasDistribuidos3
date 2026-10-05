using Foundation.Models;
using System.Collections.Concurrent;

namespace MS.Pagamento.Services;

public class PagamentoManager
{
    public ConcurrentDictionary<string, PedidoCriado> PedidosPendentes { get; } = new();
}