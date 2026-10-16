using Foundation;
using Foundation.Keys;
using Foundation.Models;
using Foundation.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace MS.Pagamento.Services;

public class PagamentoConsumerService : BackgroundService
{
    private readonly IConnection _connection;
    private readonly SignatureService _signatureService;
    private readonly KeyConfig _keyConfig;
    private readonly PagamentoManager _pagamentoManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;

    public PagamentoConsumerService(IConnection connection, SignatureService signatureService, KeyConfig keyConfig, PagamentoManager pagamentoManager, IHttpClientFactory httpClientFactory, IServiceScopeFactory scopeFactory)
    {
        _connection = connection;
        _signatureService = signatureService;
        _keyConfig = keyConfig;
        _pagamentoManager = pagamentoManager;
        _httpClientFactory = httpClientFactory;
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

        string queueName = "fila_pagamento";
        await channel.QueueDeclareAsync(
            queue: queueName, 
            durable: true, 
            exclusive: false, 
            autoDelete: false, 
            cancellationToken: ct
        );

        await channel.QueueBindAsync(
            queue: queueName, 
            exchange: "eCommerce", 
            routingKey: "pedido.estoque_ok", 
            cancellationToken: ct
        );

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            string routingKey = ea.RoutingKey;
            string jsonReceiverMessage = Encoding.UTF8.GetString(ea.Body.ToArray());

            Console.WriteLine($"[MS.Pagamento] Message received by {routingKey}");

            var eventMessage = JsonSerializer.Deserialize<Message<PedidoCriado>>(jsonReceiverMessage);
            if (eventMessage?.Content == null) 
                return;

            string producerPublicKeyPath = Path.Combine(_keyConfig.SolutionRootPath, "MS.Pagamento", "Keys", $"{eventMessage.Producer}.public.pem");

            if (!File.Exists(producerPublicKeyPath) || !_signatureService.VerifySignature(JsonSerializer.Serialize(eventMessage.Content), eventMessage.Signature, producerPublicKeyPath))
            {
                Console.WriteLine("[MS.Pagamento] Invalid signature or public key not found");
                return;
            }

            if (routingKey == "pedido.estoque_ok")
            {
                // Save Order in memory waiting for the webhook
                _pagamentoManager.PedidosPendentes.TryAdd(eventMessage.Content.Id, eventMessage.Content);
                Console.WriteLine($"[MS.Pagamento] Order {eventMessage.Content.Id} awaiting payment");

                // Call Mock Payment API to create the payment request
                var client = _httpClientFactory.CreateClient("MockPagamentoClient");
                string myWebhookUrl = "http://localhost:5207/api/webhook";

                var mockRequest = new SolicitacaoPagamentoMock(
                    PedidoId: eventMessage.Content.Id,
                    ValorTotal: eventMessage.Content.ValorTotal,
                    WebhookUrl: myWebhookUrl
                );

                try
                {
                    var response = await client.PostAsJsonAsync("/api/cobranca", mockRequest, ct);
                    var cobranca = response.IsSuccessStatusCode
                        ? await response.Content.ReadFromJsonAsync<CobrancaCriada>(ct)
                        : null;

                    if (cobranca != null && !string.IsNullOrEmpty(cobranca.CheckoutUrl))
                    {
                        Console.WriteLine($"[MS.Pagamento] Checkout URL received from Mock: {cobranca.CheckoutUrl}");

                        // Publish the checkout URL so the gateway can send it to the client over SSE
                        var pendente = new PagamentoPendente
                        {
                            PedidoId = eventMessage.Content.Id,
                            ClienteId = eventMessage.Content.ClienteId,
                            ValorTotal = eventMessage.Content.ValorTotal,
                            CheckoutUrl = cobranca.CheckoutUrl
                        };

                        using var scope = _scopeFactory.CreateScope();
                        var publisher = scope.ServiceProvider.GetRequiredService<RabbitMqPublisher>();
                        await publisher.PublishEventAsync(pendente, "pagamento.pendente");
                    }
                    else
                    {
                        Console.WriteLine($"[MS.Pagamento] Error on get Payment Mock response, status: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MS.Pagamento] Error on connect to Mock: {ex.Message}");
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
}