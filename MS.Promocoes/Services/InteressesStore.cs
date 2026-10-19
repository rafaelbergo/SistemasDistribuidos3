using Foundation.Models;
using System.Text.Json;

namespace MS.Promocoes.Services;

// One MS.Promocoes instance owns this file; writes and broker acknowledgements
// happen in that order, so redeliveries after a restart are harmless.
public class InteressesStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private Estado _estado;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public InteressesStore(string path)
    {
        _path = Path.GetFullPath(path);
        _estado = File.Exists(_path)
            ? JsonSerializer.Deserialize<Estado>(File.ReadAllText(_path))
                ?? throw new InvalidDataException("Cadastro de interesses inválido.")
            : new Estado();
    }

    public bool Aplicar(InteressePromocao interesse, string messageId)
    {
        interesse = interesse.Normalizar();
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        lock (_lock)
        {
            if (_estado.MensagensProcessadas.Contains(messageId)) return false;

            // Publish the new in-memory state only after the atomic file replacement.
            var proximo = new Estado
            {
                Interesses = new(_estado.Interesses),
                MensagensProcessadas = new(_estado.MensagensProcessadas)
            };
            if (interesse.Cancelado)
                proximo.Interesses.Remove(interesse.Email);
            else
                proximo.Interesses[interesse.Email] = interesse;
            proximo.MensagensProcessadas.Add(messageId);

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temporario = _path + ".tmp";
            File.WriteAllText(temporario, JsonSerializer.Serialize(proximo, JsonOptions));
            File.Move(temporario, _path, overwrite: true);
            _estado = proximo;
            return true;
        }
    }

    public IReadOnlyList<string> ListarDestinatarios(string categoria)
    {
        lock (_lock)
            return _estado.Interesses.Values.Where(i => i.Categorias.Contains(categoria))
                .Select(i => i.Email).Order().ToArray();
    }

    public class Estado
    {
        public Dictionary<string, InteressePromocao> Interesses { get; set; } = [];
        public HashSet<string> MensagensProcessadas { get; set; } = [];
    }
}
