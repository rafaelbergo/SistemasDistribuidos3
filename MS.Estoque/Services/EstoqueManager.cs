using Foundation.Models;
using System.Globalization;

namespace MS.Estoque.Services;


public class EstoqueManager
{
    public string InventoryFilePath { get; }
    public object InventoryLock { get; } = new object();
    public Dictionary<string, ProdutoEstoque> Inventory { get; private set; }
    public Dictionary<string, List<ItemPedido>> Reservations { get; } = [];
    public HashSet<string> ProcessedOrders { get; } = [];

    public EstoqueManager(string inventoryFilePath)
    {
        InventoryFilePath = inventoryFilePath;
        Inventory = LoadInventory(InventoryFilePath);

        Console.WriteLine($"[MS.Estoque] Estoque carregado de {InventoryFilePath}");
        foreach (var product in Inventory.OrderBy(p => p.Key))
        {
            Console.WriteLine($"[MS.Estoque] Produto {product.Key}: {product.Value.Quantidade} unidade(s), Preço: {product.Value.Preco}€");
        }
    }

    private Dictionary<string, ProdutoEstoque> LoadInventory(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("[MS.Estoque] Estoque.ini not found", filePath);

        var inventory = new Dictionary<string, ProdutoEstoque>(StringComparer.OrdinalIgnoreCase);
        bool inInventorySection = false;

        foreach (string rawLine in File.ReadLines(filePath))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith(';') || line.StartsWith('#')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inInventorySection = line.Equals("[Estoque]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inInventorySection) continue;

            // Divide value in Quantidade;Preço
            string[] parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
                throw new InvalidDataException($"Invalid line: {rawLine}");

            string[] valores = parts[1].Split(';');
            if (valores.Length != 2 ||
                !int.TryParse(valores[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int quantity) ||
                !decimal.TryParse(valores[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal price) ||
                quantity < 0 || price < 0)
            {
                throw new InvalidDataException($"Invalid values for product {parts[0]}: {parts[1]}");
            }

            if (!inventory.TryAdd(parts[0], new ProdutoEstoque { Quantidade = quantity, Preco = price }))
                throw new InvalidDataException($"Duplicate product: {parts[0]}");
        }
        return inventory;
    }

    public void SaveInventory()
    {
        var lines = new List<string> { "[Estoque]" };
        lines.AddRange(Inventory.OrderBy(p => p.Key).Select(p =>
            $"{p.Key}={p.Value.Quantidade.ToString(CultureInfo.InvariantCulture)};{p.Value.Preco.ToString(CultureInfo.InvariantCulture)}"));

        File.WriteAllLines(InventoryFilePath, lines);
        Console.WriteLine("[MS.Estoque] Estoque.ini updated");
    }
}