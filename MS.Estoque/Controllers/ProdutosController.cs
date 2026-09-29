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
                    QuantidadeDisponivel = item.Value
                }).ToList();

            return Ok(catalogo);
        }
    }
}