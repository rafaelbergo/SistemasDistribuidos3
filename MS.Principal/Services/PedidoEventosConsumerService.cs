using Foundation;
using Foundation.Keys;
using Foundation.Models;
using Foundation.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace MS.Principal.Services;

// Consumes every order status event from Estoque, Pagamento and Entrega and
// forwards it to the order's client through the SSE hub.
public class PedidoEventosConsumerService : BackgroundService
{
    private readonly IConnection _connection;
    private readonly SignatureService _signatureService;
    private readonly KeyConfig _keyConfig;
    private readonly PedidoEventosHub _hub;
    private readonly IServiceScopeFactory _scopeFactory;

    private static readonly string[] RoutingKeys =
    [
        "pedido.estoque_ok",
        "estoque.indisponivel",
        "pagamento.pendente",
        "pagamento.aprovado",
        "pagamento.recusado",
        "pedido.enviado",
    ];

    public PedidoEventosConsumerService(IConnection connection, SignatureService signatureService, KeyConfig keyConfig, PedidoEventosHub hub, IServiceScopeFactory scopeFactory)
    {
        _connection = connection;
        _signatureService = signatureService;
        _keyConfig = keyConfig;
        _hub = hub;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var channel = await _connection.CreateChannelAsync(cancellationToken: ct);

        await channel.ExchangeDeclareAsync(
            exchange: "eCommerce",
            type: ExchangeType.Direct,
            durable: true,
            cancellationToken: ct
        );

        string queueName = "fila_principal";
        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: ct
        );

        foreach (string routingKey in RoutingKeys)
        {
            await channel.QueueBindAsync(
                queue: queueName,
                exchange: "eCommerce",
                routingKey: routingKey,
                cancellationToken: ct
            );
        }

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            string routingKey = ea.RoutingKey;
            string jsonReceiverMessage = Encoding.UTF8.GetString(ea.Body.ToArray());

            Console.WriteLine($"[MS.Principal] Message received by {routingKey}");

            try
            {
                await ProcessarEventoAsync(routingKey, jsonReceiverMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MS.Principal] Error on process {routingKey}: {ex.Message}");
            }
        };

        await channel.BasicConsumeAsync(
            queue: queueName,
            autoAck: true,
            consumer: consumer,
            cancellationToken: ct
        );

        await Task.Delay(Timeout.Infinite, ct);
    }

    private async Task ProcessarEventoAsync(string routingKey, string json)
    {
        if (routingKey == "pagamento.pendente")
        {
            var message = LerMensagemAssinada<PagamentoPendente>(json);
            if (message == null)
                return;

            var pendente = message.Content;
            _hub.Publicar(pendente.ClienteId, new PedidoStatusEvento(
                pendente.PedidoId, "pagamento_pendente", "Aguardando pagamento", pendente.CheckoutUrl, message.Timestamp));
        }
        else
        {
            var message = LerMensagemAssinada<PedidoCriado>(json);
            if (message == null)
                return;

            (string status, string mensagem) = routingKey switch
            {
                "pedido.estoque_ok" => ("estoque_confirmado", "Itens reservados no estoque"),
                "estoque.indisponivel" => ("estoque_indisponivel", "Itens indisponíveis no estoque"),
                "pagamento.aprovado" => ("pagamento_aprovado", "Pagamento aprovado"),
                "pagamento.recusado" => ("pagamento_recusado", "Pagamento recusado"),
                "pedido.enviado" => ("pedido_enviado", "Nota fiscal emitida e pedido enviado"),
                _ => (routingKey, routingKey)
            };

            var pedido = message.Content;
            _hub.Publicar(pedido.ClienteId, new PedidoStatusEvento(pedido.Id, status, mensagem, null, message.Timestamp));

            // Refused payment cancels the order, so MS.Estoque releases the reserved items
            if (routingKey == "pagamento.recusado")
            {
                using var scope = _scopeFactory.CreateScope();
                var publisher = scope.ServiceProvider.GetRequiredService<RabbitMqPublisher>();
                await publisher.PublishEventAsync(pedido, "pedido.excluido");
            }
        }
    }

    // Deserializes the message and verifies its signature with the producer's public key
    private Message<T>? LerMensagemAssinada<T>(string json) where T : class
    {
        var eventMessage = JsonSerializer.Deserialize<Message<T>>(json);
        if (eventMessage?.Content == null)
            return null;

        string producerPublicKeyPath = Path.Combine(_keyConfig.SolutionRootPath, "MS.Principal", "Keys", $"{eventMessage.Producer}.public.pem");

        if (!File.Exists(producerPublicKeyPath) || !_signatureService.VerifySignature(JsonSerializer.Serialize(eventMessage.Content), eventMessage.Signature, producerPublicKeyPath))
        {
            Console.WriteLine("[MS.Principal] Invalid signature or public key not found");
            return null;
        }

        return eventMessage;
    }
}
