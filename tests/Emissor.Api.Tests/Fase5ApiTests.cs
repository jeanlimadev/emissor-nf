using System.Net;
using System.Net.Http.Json;
using Emissor.Contracts;
using Emissor.Contracts.Emitentes;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Emitentes.Tests;
using Emissor.Modules.Nfe.Tests;
using Microsoft.AspNetCore.Hosting;

namespace Emissor.Api.Tests;

[TestClass]
public sealed class Fase5ApiTests
{
    private static EmissorApiFactory _factory = null!;

    [ClassInitialize]
    public static void Up(TestContext _) => _factory = new EmissorApiFactory();

    [ClassCleanup]
    public static void Down() => _factory.Dispose();

    [TestMethod]
    public async Task Cliente_tipado_faz_o_fluxo_inteiro_e_contingencia_roteia_para_a_svc()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant cliente tipado");
        var client = new EmissorClient(_factory.CreateClient(), apiKey);

        var emitente = await client.CriarEmitenteAsync(new EmitenteRequest(TipoEmitente.PessoaJuridica, CertificadoDeTeste.CnpjValido, "87654321", "Empresa Teste LTDA", null,
            RegimeTributario.SimplesNacional, AmbienteSefaz.Homologacao, new EnderecoDto("Rua das Flores", "100", null, "Centro", "3304557", "Rio de Janeiro", "RJ", "20010000", null), null));
        Assert.IsTrue(emitente.Sucesso, emitente.Problema?.Detail);
        var id = emitente.Valor!.Id;

        Assert.IsTrue((await client.EnviarCertificadoAsync(id, CertificadoDeTeste.PfxComCn($"X:{CertificadoDeTeste.CnpjValido}"), CertificadoDeTeste.Senha)).Sucesso);
        Assert.IsTrue((await client.CriarSerieAsync(id, new SerieRequest(1, 1))).Sucesso);
        Assert.AreEqual(107, (await client.StatusSefazAsync(id)).Valor!.CStat);

        var normal = await client.EmitirAsync(id, PedidosDeTeste.PedidoSimples(), "cli-1", 30);
        Assert.IsTrue(normal.Sucesso, normal.Problema?.Detail);
        Assert.AreEqual(NfeStatus.Autorizada, normal.Valor!.Status);
        Assert.AreEqual('1', normal.Valor.ChaveAcesso![34], "tpEmis normal na chave");
        Assert.IsFalse(FakeSefazGateway.DestinosPorChave[normal.Valor.ChaveAcesso].Contingencia);

        var ligada = await client.AtivarContingenciaAsync(id, "SVRS fora do ar desde as 9h, venda nao pode esperar.");
        Assert.IsTrue(ligada.Sucesso, ligada.Problema?.Detail);
        Assert.IsTrue(ligada.Valor!.EmContingencia);

        var contingencia = await client.EmitirAsync(id, PedidosDeTeste.PedidoSimples(), "cli-2", 30);
        Assert.AreEqual(NfeStatus.Autorizada, contingencia.Valor!.Status, contingencia.Valor.XMotivo);
        Assert.AreEqual('6', contingencia.Valor.ChaveAcesso![34], "tpEmis 6 = SVC-AN");
        Assert.IsTrue(FakeSefazGateway.DestinosPorChave[contingencia.Valor.ChaveAcesso].Contingencia, "o gateway recebeu o destino de contingencia");

        var inut = await client.InutilizarAsync(id, new InutilizacaoRequest(1, 10, 12, "Numeros pulados por falha do sistema."));
        Assert.AreEqual("nfe.inutilizacao_em_contingencia", inut.Problema!.Code);

        Assert.IsFalse((await client.DesativarContingenciaAsync(id)).Valor!.EmContingencia);

        var xml = await client.ObterXmlAsync(contingencia.Valor.Id);
        StringAssert.Contains(xml.Valor, "<tpEmis>6</tpEmis>");
        StringAssert.Contains(xml.Valor, "<xJust>");

        var erro = await client.CancelarAsync(contingencia.Valor.Id, "curta");
        Assert.IsFalse(erro.Sucesso);
        Assert.AreEqual("nfe.justificativa_invalida", erro.Problema!.Code);
        Assert.AreEqual(400, erro.Problema.Status);

        var danfe = await client.ObterDanfeAsync(normal.Valor.Id);
        Assert.IsTrue(danfe.Sucesso && danfe.Valor!.Length > 5_000);
    }

    [TestMethod]
    public async Task Limite_de_requisicoes_por_chave_responde_429_com_codigo()
    {
        using var limitada = _factory.WithWebHostBuilder(b => b.UseSetting("RateLimit:PorMinuto", "3"));
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant limite");
        using var client = limitada.CreateClient().AsTenant(apiKey);

        var respostas = new List<HttpStatusCode>();

        for (var i = 0; i < 5; i++)
            respostas.Add((await client.GetAsync("/api/v1/me")).StatusCode);

        Assert.AreEqual(3, respostas.Count(s => s == HttpStatusCode.OK));
        Assert.AreEqual(2, respostas.Count(s => s == HttpStatusCode.TooManyRequests));

        var bloqueada = await client.GetAsync("/api/v1/me");
        StringAssert.Contains(await bloqueada.Content.ReadAsStringAsync(), "requisicao.limite_atingido");

        // Outra chave tem a sua propria cota.
        var (_, outraChave) = await _factory.NovoTenantComChaveAsync("Tenant limite 2");
        using var outro = limitada.CreateClient().AsTenant(outraChave);
        Assert.AreEqual(HttpStatusCode.OK, (await outro.GetAsync("/api/v1/me")).StatusCode);
    }

    [TestMethod]
    public async Task Openapi_publicado_e_health_relata_certificado_vencendo()
    {
        using var client = _factory.CreateClient();
        var openapi = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.AreEqual(HttpStatusCode.OK, openapi.StatusCode, "o documento OpenAPI sai em todo ambiente");
        StringAssert.Contains(await openapi.Content.ReadAsStringAsync(), "/api/v1/nfe/{id}/cancelamento");

        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant cert vencendo");
        using var tenant = _factory.CreateClient().AsTenant(apiKey);
        var emitente = await tenant.CriarEmitenteAsync(ApiClientExtensions.EmitentePf(CertificadoDeTeste.CpfValido));
        using var form = ApiClientExtensions.CertificadoForm(CertificadoDeTeste.PfxComCn($"X:{CertificadoDeTeste.CpfValido}", DateTimeOffset.UtcNow.AddDays(10)), CertificadoDeTeste.Senha);
        Assert.AreEqual(HttpStatusCode.OK, (await tenant.PutAsync($"/api/v1/emitentes/{emitente.Id}/certificado", form)).StatusCode);

        var health = await client.GetAsync("/health");
        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
        Assert.AreEqual("Degraded", await health.Content.ReadAsStringAsync(), "certificado vencendo em 10 dias degrada o health");
    }
}
