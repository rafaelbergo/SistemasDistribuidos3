using Foundation.Models;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MS.Promocoes.Services;

public record EmailOptions(string ApiKey, string From, Uri Endpoint, int IntervaloMs = 600)
{
    public bool Configurado => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(From);

    public static EmailOptions Carregar()
    {
        var endpoint = new Uri(Environment.GetEnvironmentVariable("Email__Endpoint") ?? "https://api.resend.com/emails");
        if (endpoint.Scheme != "https" && !(endpoint.Scheme == "http" && endpoint.IsLoopback))
            throw new ArgumentException("Email__Endpoint deve usar HTTPS (HTTP apenas para testes locais).");
        string from = Environment.GetEnvironmentVariable("Email__From") ?? "";
        if (!string.IsNullOrWhiteSpace(from) && !System.Net.Mail.MailAddress.TryCreate(from, out _))
            throw new ArgumentException("Email__From deve conter um remetente válido.");
        return new(Environment.GetEnvironmentVariable("Email__ApiKey")
            ?? Environment.GetEnvironmentVariable("RESEND_API_KEY") ?? "", from, endpoint);
    }
}

public record EmailResultado(bool Sucesso, bool Cancelado, string Detalhe);

public class ResendEmailSender(HttpClient client, EmailOptions options)
{
    public async Task<EmailResultado> EnviarAsync(Promocao promocao, string email, string chave,
        Func<bool> aindaInscrito, CancellationToken ct = default)
    {
        if (!options.Configurado) return new(false, false, "Configure Email__ApiKey e Email__From.");
        string desconto = promocao.DescontoPercentual.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"));
        string assunto = $"{desconto}% OFF: {promocao.Produto}".Replace('\r', ' ').Replace('\n', ' ');
        string texto = $"{promocao.Produto}\nCategoria: {promocao.Categoria}\nDesconto: {desconto}%\n{promocao.Descricao}\n\nGerencie ou cancele sua inscrição na aba Promoções da loja.";
        string html = $"<h1>{WebUtility.HtmlEncode(promocao.Produto)}</h1><p>Categoria: {WebUtility.HtmlEncode(promocao.Categoria)}</p><p><strong>{desconto}% OFF</strong></p><p>{WebUtility.HtmlEncode(promocao.Descricao)}</p><p>Gerencie ou cancele sua inscrição na aba Promoções da loja.</p>";
        var body = new { from = options.From, to = new[] { email }, subject = assunto, text = texto, html };
        TimeSpan espera = TimeSpan.FromMilliseconds(options.IntervaloMs);
        string erro = "API indisponível";
        for (int tentativa = 0; tentativa < 3; tentativa++)
        {
            if (espera > TimeSpan.Zero) await Task.Delay(espera, ct);
            // Recheck after delays so a cancellation also stops pending retries.
            if (!aindaInscrito()) return new(false, true, "Inscrição cancelada ou categoria removida.");
            using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            request.Headers.Add("Idempotency-Key", chave);
            request.Content = JsonContent.Create(body);
            try
            {
                using var response = await client.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    if (result.RootElement.ValueKind == JsonValueKind.Object &&
                        result.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()))
                        return new(true, false, "Aceito pela API de e-mail.");
                    erro = "API respondeu sem identificação de envio.";
                }
                else
                {
                    // Do not log response bodies: they may include addresses or credentials.
                    erro = $"API de e-mail respondeu HTTP {(int)response.StatusCode}.";
                    if (response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500)
                        return new(false, false, erro);
                }
                var retryAfter = response.Headers.RetryAfter;
                espera = retryAfter?.Delta ?? (retryAfter?.Date - DateTimeOffset.UtcNow)
                    ?? TimeSpan.FromMilliseconds(Math.Max(options.IntervaloMs, 1000 * (tentativa + 1)));
                if (espera < TimeSpan.Zero) espera = TimeSpan.Zero;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException || ex is TaskCanceledException && !ct.IsCancellationRequested)
            {
                erro = "Falha de conexão, timeout ou resposta inválida da API de e-mail.";
                espera = TimeSpan.FromMilliseconds(Math.Max(options.IntervaloMs, 1000 * (tentativa + 1)));
            }
        }
        return new(false, false, erro);
    }
}
