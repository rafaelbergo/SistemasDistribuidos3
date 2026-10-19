using System.Net.Mail;

namespace Foundation.Models;

public class InteressePromocao
{
    public string Email { get; set; } = string.Empty;
    public List<string> Categorias { get; set; } = [];
    public bool Cancelado { get; set; }

    public InteressePromocao Normalizar()
    {
        string email = Email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (email.Length > 254 || !MailAddress.TryCreate(email, out var address) ||
            address.Address != email || !address.Host.Contains('.'))
            throw new ArgumentException("Informe um e-mail válido.");

        if (Categorias == null || (Cancelado ? Categorias.Count != 0 : Categorias.Count == 0))
            throw new ArgumentException("Selecione ao menos uma categoria; cancelamentos não devem incluir categorias.");

        var categorias = Categorias.Select(c => c?.Trim().ToUpperInvariant()).ToList();
        if (categorias.Any(c => c is not ("A" or "B" or "C")))
            throw new ArgumentException("Categorias permitidas: A, B e C.");

        return new InteressePromocao
        {
            Email = email,
            Categorias = categorias.Select(c => c!).Distinct().Order().ToList(),
            Cancelado = Cancelado
        };
    }
}

public class InteressePromocaoRequest
{
    public string Email { get; set; } = string.Empty;
    public List<string> Categorias { get; set; } = [];
}
