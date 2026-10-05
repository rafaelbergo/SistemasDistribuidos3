using Microsoft.AspNetCore.Mvc;

namespace MS.Principal.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CatalogoController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public CatalogoController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> GetProdutosDisponiveis()
    {
        var client = _httpClientFactory.CreateClient("EstoqueClient");

        try
        {
            // GET Request to MS.Estoque produtos endpoint
            var response = await client.GetAsync("/api/produtos");

            if (response.IsSuccessStatusCode)
            {
                // Get response content as JSON to return to frontend
                var produtos = await response.Content.ReadAsStringAsync();
                return Content(produtos, "application/json");
            }

            Console.WriteLine($"[MS.Principal] Error on request to MS.Estoque produtos endpoint, status: {response.StatusCode}");
            return StatusCode((int)response.StatusCode, "Error on request to MS.Estoque produtos endpoint");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MS.Principal] Error on connect to MS.Estoque: {ex.Message}");
            return StatusCode(500, "Error on connect to MS.Estoque");
        }
    }
}