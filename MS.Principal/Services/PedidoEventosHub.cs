using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MS.Principal.Services;

// Status change sent to the frontend over SSE
public record PedidoStatusEvento(string PedidoId, string Status, string Mensagem, string? CheckoutUrl, DateTime Timestamp);

// Event with its per-client sequential id (used as the SSE "id:" field)
public record PedidoEventoSse(long Id, PedidoStatusEvento Evento);

// Keeps the order status history of each client and pushes new events to
// every open SSE connection of that client. The history lets a client that
// connects later (or reconnects) receive what it missed.
public class PedidoEventosHub
{
    private const int MaxHistoricoPorCliente = 200;

    private readonly ConcurrentDictionary<string, ClienteEventos> _clientes = new(StringComparer.OrdinalIgnoreCase);

    public void Publicar(string clienteId, PedidoStatusEvento evento)
    {
        var cliente = _clientes.GetOrAdd(clienteId, _ => new ClienteEventos());

        lock (cliente.Lock)
        {
            var item = new PedidoEventoSse(++cliente.UltimoId, evento);

            cliente.Historico.Add(item);
            if (cliente.Historico.Count > MaxHistoricoPorCliente)
                cliente.Historico.RemoveAt(0);

            foreach (var assinante in cliente.Assinantes)
                assinante.Writer.TryWrite(item);
        }
    }

    // Returns the events after lastEventId and a reader for the next ones.
    // Both are taken under the same lock, so no event is lost or duplicated.
    public Assinatura Assinar(string clienteId, long lastEventId)
    {
        var cliente = _clientes.GetOrAdd(clienteId, _ => new ClienteEventos());
        var canal = Channel.CreateUnbounded<PedidoEventoSse>();

        lock (cliente.Lock)
        {
            var pendentes = cliente.Historico.Where(e => e.Id > lastEventId).ToList();
            cliente.Assinantes.Add(canal);

            return new Assinatura(pendentes, canal.Reader, () =>
            {
                lock (cliente.Lock)
                    cliente.Assinantes.Remove(canal);
            });
        }
    }

    private class ClienteEventos
    {
        public object Lock { get; } = new();
        public long UltimoId { get; set; }
        public List<PedidoEventoSse> Historico { get; } = [];
        public List<Channel<PedidoEventoSse>> Assinantes { get; } = [];
    }
}

public sealed class Assinatura(IReadOnlyList<PedidoEventoSse> pendentes, ChannelReader<PedidoEventoSse> novos, Action cancelar) : IDisposable
{
    public IReadOnlyList<PedidoEventoSse> Pendentes { get; } = pendentes;
    public ChannelReader<PedidoEventoSse> Novos { get; } = novos;

    public void Dispose() => cancelar();
}
