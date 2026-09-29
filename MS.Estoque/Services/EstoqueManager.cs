using Foundation.Models;
using System.Globalization;

namespace MS.Estoque.Services;

public class EstoqueManager
{
    public string InventoryFilePath { get; }
    public object InventoryLock { get; } = new object();
    public Dictionary<string, int> Inventory { get; private set; }
    public Dictionary<string, List<ItemPedido>> Reservations { get; } = [];
    public HashSet<string> ProcessedOrders { get; } = [];

    public EstoqueManager(string inventoryFilePath)
    {
        InventoryFilePath = inventoryFilePath;
        Inventory = LoadInventory(InventoryFilePath);

        Console.WriteLine($"[MS.Estoque] Estoque carregado de {InventoryFilePath}");
        foreach (var product in Inventory.OrderBy(p => p.Key))
        {
            Console.WriteLine($"[MS.Estoque] Produto {product.Key}: {product.Value} unidades");
        }
    }

    private Dictionary<string, int> LoadInventory(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("[MS.Estoque] Estoque.ini not found", filePath);

        var inventory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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

            string[] parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int quantity) || quantity < 0)
            {
                throw new InvalidDataException($"[MS.Estoque] Invalid .ini line: {rawLine}");
            }

            if (!inventory.TryAdd(parts[0], quantity))
                throw new InvalidDataException($"[MS.Estoque] Product duplicated: {parts[0]}");
        }
        return inventory;
    }

    public void SaveInventory()
    {
        var lines = new List<string> { "[Estoque]" };
        lines.AddRange(Inventory.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value.ToString(CultureInfo.InvariantCulture)}"));

        File.WriteAllLines(InventoryFilePath, lines);
        Console.WriteLine("[MS.Estoque] Estoque.ini updated");
    }
}