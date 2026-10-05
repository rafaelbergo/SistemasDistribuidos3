using Foundation;
using Foundation.Keys;
using MS.Pagamento.Services;
using RabbitMQ.Client;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

string solutionRootPath = Directory.GetParent(builder.Environment.ContentRootPath)?.FullName ?? builder.Environment.ContentRootPath;

var keyManagement = new KeyManagement(solutionRootPath, "MS.Principal");
keyManagement.CheckKeys();

var signature = new SignatureService();
string privateKeyPath = Path.Combine(solutionRootPath, "MS.Principal", "Keys", "MS.Principal.private.pem");
builder.Services.AddSingleton(signature);
builder.Services.AddSingleton(new KeyConfig { PrivateKeyPath = privateKeyPath });

var factory = new ConnectionFactory { HostName = "localhost" };
var rabbitConnection = await factory.CreateConnectionAsync();
builder.Services.AddSingleton(rabbitConnection);
builder.Services.AddScoped<RabbitMqPublisher>();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
