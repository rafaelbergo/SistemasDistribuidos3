namespace Foundation.Models;

public class ProdutoCatalogoDto
{
    public string Id { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public int QuantidadeDisponivel { get; set; }
    public decimal Preco { get; set; }
}