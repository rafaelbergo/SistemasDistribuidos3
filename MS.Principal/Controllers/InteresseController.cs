using Foundation.Models;
using Foundation.Services;
using Microsoft.AspNetCore.Mvc;

namespace MS.Principal.Controllers;

[ApiController]
[Route("api/interesse")]
public class InteresseController(RabbitMqPublisher publisher, ILogger<InteresseController> logger) : ControllerBase
{
    [HttpPost]
    public Task<IActionResult> Registrar([FromBody] InteressePromocaoRequest request) =>
        PublicarAsync(new InteressePromocao { Email = request.Email, Categorias = request.Categorias });

    [HttpDelete("{email}")]
    public Task<IActionResult> Cancelar(string email) =>
        PublicarAsync(new InteressePromocao { Email = email, Cancelado = true });

    private async Task<IActionResult> PublicarAsync(InteressePromocao interesse)
    {
        try
        {
            interesse = interesse.Normalizar();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        try
        {
            string routingKey = interesse.Cancelado ? "interesse.cancelado" : "interesse.promocao";
            await publisher.PublishEventAsync(interesse, routingKey);
            return Accepted(new
            {
                mensagem = "Solicitação enviada para processamento.",
                email = interesse.Email,
                categorias = interesse.Categorias
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao publicar interesse em promoções");
            return StatusCode(503, "Não foi possível enviar a solicitação. Tente novamente.");
        }
    }
}
