using Foundation;
using Foundation.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Security.Cryptography;
using System.Text.Json;

namespace MS.Promocoes.Services;

public sealed class InteresseConsumer(IConnection connection, InteressesStore store,
    SignatureService signatures, string solutionRoot) : IAsyncDisposable
{
    private IChannel? _channel;

    public async Task IniciarAsync()
    {
        _channel = await connection.CreateChannelAsync();
        await _channel.ExchangeDeclareAsync("eCommerce", ExchangeType.Direct, durable: true);
        await _channel.QueueDeclareAsync("fila_interesses_promocoes", durable: true, exclusive: false, autoDelete: false);
        foreach (string key in new[] { "interesse.promocao", "interesse.cancelado" })
            await _channel.QueueBindAsync("fila_interesses_promocoes", "eCommerce", key);
        await _channel.BasicQosAsync(0, 1, false);
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                var message = JsonSerializer.Deserialize<Message<InteressePromocao>>(delivery.Body.Span);
                if (message?.Content == null || message.Producer != "MS.Principal" || string.IsNullOrWhiteSpace(message.MessageId))
                    throw new JsonException("Evento de interesse inválido.");

                var interesse = message.Content;
                if (delivery.RoutingKey != (interesse.Cancelado ? "interesse.cancelado" : "interesse.promocao"))
                    throw new JsonException("Evento incompatível com a routing key.");
                string publicKey = Path.Combine(solutionRoot, "MS.Promocoes", "Keys", "MS.Principal.public.pem");
                if (!signatures.VerifySignature(JsonSerializer.Serialize(interesse), message.Signature, publicKey))
                    throw new CryptographicException("Assinatura inválida.");

                store.Aplicar(interesse, message.MessageId);
                await _channel.BasicAckAsync(delivery.DeliveryTag, false);
                Console.WriteLine($"[MS.Promocoes] Interesse {(interesse.Cancelado ? "cancelado" : "registrado/atualizado")}.");
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException or CryptographicException)
            {
                Console.WriteLine($"[MS.Promocoes] Interesse descartado: {ex.Message}");
                await _channel.BasicNackAsync(delivery.DeliveryTag, false, requeue: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[MS.Promocoes] Falha ao persistir/ler chaves; tentando novamente: {ex.Message}");
                await Task.Delay(1000);
                await _channel.BasicNackAsync(delivery.DeliveryTag, false, requeue: true);
            }
        };
        await _channel.BasicConsumeAsync("fila_interesses_promocoes", autoAck: false, consumer: consumer);
        Console.WriteLine("[MS.Promocoes] Consumidor de interesses iniciado.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel != null) await _channel.DisposeAsync();
    }
}
