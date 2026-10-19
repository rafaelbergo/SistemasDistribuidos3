using Foundation.Models;

namespace MS.Promocoes.Services;

public class PromocaoEmailDispatcher(InteressesStore interesses, EmailEnviosStore envios, ResendEmailSender sender)
{
    public async Task<bool> EnviarAsync(Promocao promocao)
    {
        bool sucesso = true;
        foreach (string email in interesses.ListarDestinatarios(promocao.Categoria))
        {
            string chave = EmailEnviosStore.Chave(promocao.Id, email);
            if (envios.JaEnviado(chave)) continue;
            var resultado = await sender.EnviarAsync(promocao, email, chave,
                () => interesses.ListarDestinatarios(promocao.Categoria).Contains(email));
            if (resultado.Sucesso) envios.MarcarEnviado(chave);
            else if (!resultado.Cancelado) sucesso = false;
            Console.WriteLine($"[MS.Promocoes] Envio {chave[..12]}: {resultado.Detalhe}");
        }
        return sucesso;
    }
}
