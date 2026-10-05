namespace Foundation.Models;

public class PedidoCriado
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClienteId { get; set; } = string.Empty;
    public List<ItemPedido> Itens { get; set; } = [];
    public decimal ValorTotal { get; set; } = 0;
}

public class ItemPedido
{
    public string Id { get; set; } = string.Empty;
    public int Quantidade { get; set; } = 1;
}