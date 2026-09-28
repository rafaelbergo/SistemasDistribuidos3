using Foundation;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace MS.Principal.Services;

public class KeyConfig
{
    public string PrivateKeyPath { get; set; } = string.Empty;
}

public class RabbitMqPublisher
{
    private readonly IConnection _connection;
    private readonly SignatureService _signatureService;
    private readonly string _privateKeyPath;

    public RabbitMqPublisher(IConnection connection, SignatureService signatureService, KeyConfig keyConfig)
    {
        _connection = connection;
        _signatureService = signatureService;
        _privateKeyPath = keyConfig.PrivateKeyPath;
    }

    public async Task PublishEventAsync<T>(T pedido, string routingKey, string exchange = "eCommerce")
    {
        await using var channel = await _connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync(exchange: exchange, type: ExchangeType.Direct);

        string responseJson = JsonSerializer.Serialize(pedido);
        string signature = _signatureService.SignText(responseJson, _privateKeyPath);

        var responseMessage = new Message<T>
        {
            Producer = "MS.Principal",
            Content = pedido,
            Signature = signature
        };

        byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responseMessage));

        await channel.BasicPublishAsync(
            exchange: exchange,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: new BasicProperties(),
            body: body
        );

        Console.WriteLine($"[MS.Principal] Event {routingKey} published");
    }
}