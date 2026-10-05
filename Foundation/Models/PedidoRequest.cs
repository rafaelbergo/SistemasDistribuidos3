namespace Foundation.Models;

public class PedidoRequest
{
    public string ClienteId { get; set; } = string.Empty;
    public List<ItemRequest> Itens { get; set; } = [];
}

public class ItemRequest
{
    public string Id { get; set; } = string.Empty;
    public int Quantidade { get; set; }
}