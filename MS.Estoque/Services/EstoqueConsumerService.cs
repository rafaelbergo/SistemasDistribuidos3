using Foundation;
using Foundation.Keys;
using Foundation.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace MS.Estoque.Services;

public class EstoqueConsumerService : BackgroundService
{
    private readonly IConnection _connection;
    private readonly SignatureService _signatureService;
    private readonly KeyConfig _keyConfig;
    private readonly EstoqueManager _estoqueManager;

    public EstoqueConsumerService(IConnection connection, SignatureService signatureService, KeyConfig keyConfig, EstoqueManager estoqueManager)
    {
        _connection = connection;
        _signatureService = signatureService;
        _keyConfig = keyConfig;
        _estoqueManager = estoqueManager;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Create Channel
        var channel = await _connection.CreateChannelAsync(cancellationToken: ct);

        // Create Exchange
        await channel.ExchangeDeclareAsync(
            exchange: "eCommerce",
            type: ExchangeType.Direct,
            durable: true, 
            cancellationToken: ct
        );

        // Create Queue
        string queueName = "fila_estoque";
        await channel.QueueDeclareAsync(
            queue: queueName, 
            durable: true, 
            exclusive: false, 
            autoDelete: false, 
            cancellationToken: ct
        );

        // Bind queues
        await channel.QueueBindAsync(
            queue: queueName, 
            exchange: "eCommerce", 
            routingKey: "pedido.criado", 
            cancellationToken: ct
        );

        await channel.QueueBindAsync(
            queue: queueName, 
            exchange: "eCommerce", 
            routingKey: "pedido.excluido", 
            cancellationToken: ct
        );

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            string routingKey = ea.RoutingKey;
            string jsonReceiverMessage = Encoding.UTF8.GetString(ea.Body.ToArray());

            Console.WriteLine($"[MS.Estoque] Message received from {routingKey}");

            var eventMessage = JsonSerializer.Deserialize<Message<PedidoCriado>>(jsonReceiverMessage);
            if (eventMessage?.Content == null) return;

            string producerPublicKeyPath = Path.Combine(_keyConfig.SolutionRootPath, "MS.Estoque", "Keys", $"{eventMessage.Producer}.public.pem");
            if (!File.Exists(producerPublicKeyPath) || !_signatureService.VerifySignature(JsonSerializer.Serialize(eventMessage.Content), eventMessage.Signature, producerPublicKeyPath))
            {
                Console.WriteLine("[MS.Estoque] Invalid signature or key not found");
                return;
            }

            if (routingKey == "pedido.criado")
            {
                bool itemsAvailable;
                lock (_estoqueManager.InventoryLock)
                {
                    if (!_estoqueManager.ProcessedOrders.Add(eventMessage.Content.Id)) 
                        return;

                    itemsAvailable = eventMessage.Content.Itens.All(item =>
                        _estoqueManager.Inventory.TryGetValue(item.Id, out int available) && available >= item.Quantidade);

                    if (itemsAvailable)
                    {
                        foreach (var item in eventMessage.Content.Itens)
                        {
                            _estoqueManager.Inventory[item.Id] -= item.Quantidade;
                            Console.WriteLine($"[MS.Estoque] Removed {item.Quantidade} of item {item.Id} , Remaining: {_estoqueManager.Inventory[item.Id]}");
                        }
                        _estoqueManager.Reservations[eventMessage.Content.Id] = eventMessage.Content.Itens;
                        _estoqueManager.SaveInventory();
                    }
                }

                // Publish success event
                if (itemsAvailable)
                {
                    Console.WriteLine($"[MS.Estoque] Order {eventMessage.Content.Id} processed, items available");
                    await PublishEventAsync(channel, "pedido.estoque_ok", eventMessage.Content);
                }
                
                // Publish failure event
                else
                {
                    Console.WriteLine($"[MS.Estoque] Order {eventMessage.Content.Id} cancelled, items not available");
                    await PublishEventAsync(channel, "estoque.indisponivel", eventMessage.Content);
                }
            }
            else if (routingKey == "pedido.excluido")
            {
                lock (_estoqueManager.InventoryLock)
                {
                    if (_estoqueManager.Reservations.Remove(eventMessage.Content.Id, out var reservedItems))
                    {
                        foreach (var item in reservedItems)
                        {
                            if (_estoqueManager.Inventory.ContainsKey(item.Id))
                                _estoqueManager.Inventory[item.Id] += item.Quantidade;
                        }
                        _estoqueManager.SaveInventory();
                    }
                }
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

    private async Task PublishEventAsync<T>(IChannel channel, string routingKey, T pedido)
    {
        string responseJson = JsonSerializer.Serialize(pedido);
        string signature = _signatureService.SignText(responseJson, _keyConfig.PrivateKeyPath);

        var responseMessage = new Message<T>
        {
            Producer = "MS.Estoque",
            Content = pedido,
            Signature = signature
        };

        byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responseMessage));
        
        await channel.BasicPublishAsync(
            exchange: "eCommerce", 
            routingKey: routingKey,
            body: body
        );
        
        Console.WriteLine($"[MS.Estoque] Event {routingKey} published");
    }
}