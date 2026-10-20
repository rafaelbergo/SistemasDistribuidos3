using Foundation;
using Foundation.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Security.Cryptography;
using System.Text.Json;

namespace MS.Promocoes.Services;

public sealed class PromocaoEmailConsumer(IConnection connection, PromocaoEmailDispatcher dispatcher,
    SignatureService signatures, string publicKeyPath) : IAsyncDisposable
{
    private IChannel? _channel;

    public async Task IniciarAsync(bool configurado)
    {
        _channel = await connection.CreateChannelAsync();
        await _channel.ExchangeDeclareAsync("Promoções", ExchangeType.Topic, durable: true);
        await _channel.ExchangeDeclareAsync("promocoes.email.falhas", ExchangeType.Direct, durable: true);
        await _channel.QueueDeclareAsync("fila_email_falhas", durable: true, exclusive: false, autoDelete: false);
        await _channel.QueueBindAsync("fila_email_falhas", "promocoes.email.falhas", "email.falhou");
        await _channel.QueueDeclareAsync("fila_email_promocoes", durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = "promocoes.email.falhas",
                ["x-dead-letter-routing-key"] = "email.falhou"
            });
        await _channel.QueueBindAsync("fila_email_promocoes", "Promoções", "promocao.categoria.*");
        if (!configurado)
        {
            Console.WriteLine("[MS.Promocoes] E-mail aguardando configuração: Email.ApiKey e Email.From no appsettings.json ou nas variáveis de ambiente. Promoções permanecerão na fila.");
            return;
        }
        await _channel.BasicQosAsync(0, 1, false);
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                var message = JsonSerializer.Deserialize<Message<Promocao>>(delivery.Body.Span);
                var promocao = message?.Content;
                if (promocao == null || message!.Producer != "MS.Promocoes" ||
                    string.IsNullOrWhiteSpace(promocao.Id) || string.IsNullOrWhiteSpace(promocao.Produto) ||
                    promocao.Categoria is not ("A" or "B" or "C") || promocao.DescontoPercentual is <= 0 or > 100 ||
                    delivery.RoutingKey != $"promocao.categoria.{promocao.Categoria}")
                    throw new JsonException("Promoção inválida.");
                if (!signatures.VerifySignature(JsonSerializer.Serialize(promocao), message.Signature, publicKeyPath))
                    throw new CryptographicException("Assinatura inválida.");

                if (await dispatcher.EnviarAsync(promocao))
                    await _channel.BasicAckAsync(delivery.DeliveryTag, false);
                else
                {
                    Console.WriteLine("[MS.Promocoes] Falhas de envio encaminhadas para fila_email_falhas.");
                    await _channel.BasicNackAsync(delivery.DeliveryTag, false, requeue: false);
                }
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException or CryptographicException)
            {
                Console.WriteLine($"[MS.Promocoes] Promoção rejeitada: {ex.Message}");
                await _channel.BasicNackAsync(delivery.DeliveryTag, false, requeue: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine("[MS.Promocoes] Falha no histórico de envios; tentando novamente.");
                await Task.Delay(1000);
                await _channel.BasicNackAsync(delivery.DeliveryTag, false, requeue: true);
            }
        };
        await _channel.BasicConsumeAsync("fila_email_promocoes", autoAck: false, consumer: consumer);
        Console.WriteLine("[MS.Promocoes] Consumidor de e-mail iniciado.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel != null) await _channel.DisposeAsync();
    }
}
