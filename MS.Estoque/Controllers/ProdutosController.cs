using Microsoft.AspNetCore.Mvc;
using MS.Estoque.Services;

namespace MS.Estoque.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProdutosController : ControllerBase
{
    private readonly EstoqueManager _estoqueManager;

    public ProdutosController(EstoqueManager estoqueManager)
    {
        _estoqueManager = estoqueManager;
    }

    [HttpGet]
    public IActionResult GetCatalogo()
    {
        lock (_estoqueManager.InventoryLock)
        {
            var catalogo = _estoqueManager.Inventory
                .OrderBy(item => item.Key)
                .Select(item => new
                {
                    Id = item.Key,
                    Descricao = $"Produto {item.Key}",
                    QuantidadeDisponivel = item.Value.Quantidade,
                    Preco = item.Value.Preco
                }).ToList();

            return Ok(catalogo);
        }
    }

    [HttpGet("{id}")]
    public IActionResult GetProduto(string id)
    {
        lock (_estoqueManager.InventoryLock)
        {
            if (_estoqueManager.Inventory.TryGetValue(id, out var produto))
            {
                return Ok(new
                {
                    Id = id,
                    Descricao = $"Produto {id}",
                    QuantidadeDisponivel = produto.Quantidade,
                    Preco = produto.Preco
                });
            }
            return NotFound();
        }
    }
}