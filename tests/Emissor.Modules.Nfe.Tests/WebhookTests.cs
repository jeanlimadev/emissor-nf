using System.Text;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Nfe.Domain;
using Emissor.Modules.Nfe.Infrastructure.Webhooks;

namespace Emissor.Modules.Nfe.Tests;

[TestClass]
public sealed class WebhookTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Assinatura_hmac_sha256_confere_e_recusa_corpo_alterado()
    {
        var segredo = Encoding.UTF8.GetBytes("segredo-de-teste-com-tamanho-ok");
        var corpo = Encoding.UTF8.GetBytes("""{"evento":"nfe.autorizada"}""");

        var assinatura = AssinaturaWebhook.Assinar(segredo, corpo);

        StringAssert.StartsWith(assinatura, "sha256=");
        Assert.AreEqual("sha256=".Length + 64, assinatura.Length);
        Assert.IsTrue(AssinaturaWebhook.Conferir(segredo, corpo, assinatura));
        Assert.IsFalse(AssinaturaWebhook.Conferir(segredo, Encoding.UTF8.GetBytes("""{"evento":"nfe.rejeitada"}"""), assinatura));
        Assert.IsFalse(AssinaturaWebhook.Conferir(Encoding.UTF8.GetBytes("outro-segredo-qualquer-ok"), corpo, assinatura));
        Assert.IsFalse(AssinaturaWebhook.Conferir(segredo, corpo, null));
        Assert.IsFalse(AssinaturaWebhook.Conferir(segredo, corpo, "md5=abc"));
    }

    [TestMethod]
    public void Segredo_gerado_tem_32_bytes_em_base64url()
    {
        var segredo = AssinaturaWebhook.GerarSegredo();

        Assert.AreEqual(43, segredo.Length);
        Assert.IsTrue(segredo.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));
        Assert.AreNotEqual(segredo, AssinaturaWebhook.GerarSegredo());
    }

    [TestMethod]
    public void Url_exige_https_salvo_localhost()
    {
        Assert.IsTrue(Webhook.ValidarUrl("https://erp.exemplo.com.br/webhooks/nfe").IsSuccess);
        Assert.IsTrue(Webhook.ValidarUrl("http://localhost:5000/hook").IsSuccess);
        Assert.IsTrue(Webhook.ValidarUrl("http://127.0.0.1:5000/hook").IsSuccess);
        Assert.AreEqual("webhook.url_invalida", Webhook.ValidarUrl("http://erp.exemplo.com.br/hook").Error!.Code);
        Assert.AreEqual("webhook.url_invalida", Webhook.ValidarUrl("ftp://x").Error!.Code);
        Assert.AreEqual("webhook.url_invalida", Webhook.ValidarUrl("nao e url").Error!.Code);
        Assert.AreEqual("webhook.url_invalida", Webhook.ValidarUrl("").Error!.Code);
    }

    [TestMethod]
    public void Entrega_reagenda_ate_o_limite_e_depois_falha()
    {
        var entrega = WebhookEntrega.Agendar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "nfe.autorizada", Guid.NewGuid(), "{}", Agora);
        Assert.AreEqual(WebhookEntregaStatus.Pendente, entrega.Status);
        Assert.AreEqual(Agora, entrega.ProximaTentativaEm);

        entrega.RegistrarFalha(500, "HTTP 500", TimeSpan.FromSeconds(30), Agora);
        Assert.AreEqual(WebhookEntregaStatus.Pendente, entrega.Status);
        Assert.AreEqual(Agora.AddSeconds(30), entrega.ProximaTentativaEm);
        Assert.AreEqual(500, entrega.UltimoHttpStatus);

        // Tentativas sao contadas pela fila; simulamos o limite via reflexao da regra: 10 tentativas.
        for (var i = 0; i < WebhookEntrega.MaxTentativas; i++)
            typeof(WebhookEntrega).GetProperty(nameof(WebhookEntrega.Tentativas))!.SetValue(entrega, i + 1);

        entrega.RegistrarFalha(null, "Timeout.", TimeSpan.FromSeconds(30), Agora);
        Assert.AreEqual(WebhookEntregaStatus.Falha, entrega.Status);
        Assert.IsNull(entrega.ProximaTentativaEm);

        var outra = WebhookEntrega.Agendar(Guid.NewGuid(), Guid.NewGuid(), null, "webhook.teste", Guid.NewGuid(), "{}", Agora);
        outra.RegistrarSucesso(204, Agora);
        Assert.AreEqual(WebhookEntregaStatus.Entregue, outra.Status);
        Assert.AreEqual(Agora, outra.EntregueEm);
    }

    [TestMethod]
    public void Eventos_derivam_do_status_final_da_nota()
    {
        Assert.AreEqual("nfe.autorizada", EventosWebhook.DoStatus(NfeStatus.Autorizada));
        Assert.AreEqual("nfe.rejeitada", EventosWebhook.DoStatus(NfeStatus.Rejeitada));
        Assert.AreEqual("nfe.denegada", EventosWebhook.DoStatus(NfeStatus.Denegada));
        Assert.AreEqual("nfe.falha", EventosWebhook.DoStatus(NfeStatus.Falha));
        Assert.AreEqual("nfe.cancelada", EventosWebhook.DoStatus(NfeStatus.Cancelada));
        Assert.IsNull(EventosWebhook.DoStatus(NfeStatus.Recebida));
        Assert.IsNull(EventosWebhook.DoStatus(NfeStatus.PendenteConsulta));
    }
}
