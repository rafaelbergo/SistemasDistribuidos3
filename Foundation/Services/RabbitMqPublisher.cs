using Foundation;
using Foundation.Keys;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace MS.Pagamento.Services;

public class RabbitMqPublisher
{
    private readonly IConnection _connection;
    private readonly SignatureService _signatureService;
    private readonly KeyConfig _keyConfig;

    public RabbitMqPublisher(IConnection connection, SignatureService signatureService, KeyConfig keyConfig)
    {
        _connection = connection;
        _signatureService = signatureService;
        _keyConfig = keyConfig;
    }

    public async Task PublishEventAsync<T>(T conteudo, string routingKey, string exchange = "eCommerce")
    {
        await using var channel = await _connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync(exchange: exchange, type: ExchangeType.Direct, durable: true);

        string responseJson = JsonSerializer.Serialize(conteudo);
        string signature = _signatureService.SignText(responseJson, _keyConfig.PrivateKeyPath);

        var responseMessage = new Message<T>
        {
            Producer = _keyConfig.ProducerName,
            Content = conteudo,
            Signature = signature
        };

        byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responseMessage));

        await channel.BasicPublishAsync(
            exchange: exchange,
            routingKey: routingKey,
            body: body
        );

        Console.WriteLine($"[{_keyConfig.ProducerName}] Event {routingKey} published");
    }
}