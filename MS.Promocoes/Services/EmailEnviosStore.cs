using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MS.Promocoes.Services;

public class EmailEnviosStore
{
    private readonly string _path;
    private HashSet<string> _enviados;

    public EmailEnviosStore(string path)
    {
        _path = Path.GetFullPath(path);
        _enviados = File.Exists(_path)
            ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(_path))
                ?? throw new InvalidDataException("Histórico de envios inválido.")
            : [];
    }

    public static string Chave(string promocaoId, string email) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(promocaoId + "\n" + email.Trim().ToLowerInvariant())));

    public bool JaEnviado(string chave) => _enviados.Contains(chave);

    public void MarcarEnviado(string chave)
    {
        var proximo = new HashSet<string>(_enviados) { chave };
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(proximo));
        File.Move(_path + ".tmp", _path, overwrite: true);
        _enviados = proximo;
    }
}
