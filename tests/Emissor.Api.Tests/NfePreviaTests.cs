using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using Emissor.Contracts.Emitentes;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Emitentes.Tests;
using Emissor.Modules.Nfe.Infrastructure.Assinatura;
using Emissor.Modules.Nfe.Tests;

namespace Emissor.Api.Tests;

/// <summary>
/// A previa de ponta a ponta pela API: tenant autenticado, emitente com
/// certificado e serie, pedido de emissao -> XML assinado e valido.
/// </summary>
[TestClass]
public sealed class NfePreviaTests
{
    private static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/nfe";
    private static EmissorApiFactory _factory = null!;

    [ClassInitialize]
    public static void Up(TestContext _) => _factory = new EmissorApiFactory();

    [ClassCleanup]
    public static void Down() => _factory.Dispose();

    [TestMethod]
    public async Task Previa_de_emitente_cpf_devolve_xml_assinado_sem_consumir_numero()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant previa PF");
        using var client = _factory.CreateClient().AsTenant(apiKey);
        var emitente = await client.EmitenteProntoAsync(ApiClientExtensions.EmitentePf(CertificadoDeTeste.CpfValido),
            $"PRODUTOR RURAL TESTE:{CertificadoDeTeste.CpfValido}", serie: 1, proximoNumero: 42);

        var response = await client.PostAsJsonAsync($"/api/v1/emitentes/{emitente.Id}/nfe/previa", PedidosDeTeste.PedidoRegimeNormal(serie: 1));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var previa = (await response.Content.ReadFromJsonAsync<NfePreviaResponse>())!;
        Assert.AreEqual(44, previa.ChaveAcesso.Length);
        StringAssert.StartsWith(previa.ChaveAcesso, "33");
        Assert.AreEqual(1, previa.Serie);
        Assert.AreEqual(42, previa.Numero);

        var xml = XDocument.Parse(previa.Xml);
        Assert.AreEqual(CertificadoDeTeste.CpfValido, xml.Root!.Element(Ns + "infNFe")!.Element(Ns + "emit")!.Element(Ns + "CPF")!.Value);
        Assert.IsTrue(XmlDsigNfeAssinador.Verificar(previa.Xml), "a assinatura do XML devolvido tem que conferir");

        // A previa nao consome numero: a serie continua no 42.
        var series = await client.GetFromJsonAsync<List<SerieResponse>>($"/api/v1/emitentes/{emitente.Id}/series");
        Assert.AreEqual(42, series!.Single().ProximoNumero);
    }

    [TestMethod]
    public async Task Previa_com_formato_xml_devolve_o_xml_puro()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant previa xml");
        using var client = _factory.CreateClient().AsTenant(apiKey);
        var emitente = await client.EmitenteProntoAsync(ApiClientExtensions.EmitentePf(CertificadoDeTeste.CpfValido),
            $"X:{CertificadoDeTeste.CpfValido}", serie: 1, proximoNumero: 1);

        var response = await client.PostAsJsonAsync($"/api/v1/emitentes/{emitente.Id}/nfe/previa?formato=xml", PedidosDeTeste.PedidoRegimeNormal());

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual("application/xml", response.Content.Headers.ContentType!.MediaType);
        var xml = await response.Content.ReadAsStringAsync();
        StringAssert.StartsWith(xml, "<?xml version=\"1.0\" encoding=\"UTF-8\"?><NFe");
        Assert.IsTrue(XmlDsigNfeAssinador.Verificar(xml));
    }

    [TestMethod]
    public async Task Previa_de_emitente_cnpj_simples_nacional()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant previa PJ");
        using var client = _factory.CreateClient().AsTenant(apiKey);
        var pj = new EmitenteRequest(TipoEmitente.PessoaJuridica, CertificadoDeTeste.CnpjValido, "87654321", "Empresa Teste LTDA", "Empresa Teste",
            RegimeTributario.SimplesNacional, AmbienteSefaz.Homologacao,
            new EnderecoDto("Rua das Flores", "100", null, "Centro", "3304557", "Rio de Janeiro", "RJ", "20010000", null), null);
        var emitente = await client.EmitenteProntoAsync(pj, $"EMPRESA TESTE:{CertificadoDeTeste.CnpjValido}", serie: 3, proximoNumero: 1);

        var response = await client.PostAsJsonAsync($"/api/v1/emitentes/{emitente.Id}/nfe/previa", PedidosDeTeste.PedidoSimples(serie: 3));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var previa = (await response.Content.ReadFromJsonAsync<NfePreviaResponse>())!;
        var xml = XDocument.Parse(previa.Xml);
        Assert.AreEqual(CertificadoDeTeste.CnpjValido, xml.Root!.Element(Ns + "infNFe")!.Element(Ns + "emit")!.Element(Ns + "CNPJ")!.Value);
        Assert.AreEqual("003", previa.ChaveAcesso.Substring(22, 3));
    }

    [TestMethod]
    public async Task Pedido_invalido_devolve_400_com_erros_por_campo()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant previa 400");
        using var client = _factory.CreateClient().AsTenant(apiKey);
        var emitente = await client.EmitenteProntoAsync(ApiClientExtensions.EmitentePf(CertificadoDeTeste.CpfValido),
            $"X:{CertificadoDeTeste.CpfValido}", serie: 1, proximoNumero: 1);

        var pedido = PedidosDeTeste.PedidoRegimeNormal();
        var item = pedido.Itens[0];
        pedido = pedido with
        {
            Identificacao = pedido.Identificacao with { NaturezaOperacao = "" },
            Itens = [item with { Produto = item.Produto with { Ncm = "123" } }]
        };

        var response = await client.PostAsJsonAsync($"/api/v1/emitentes/{emitente.Id}/nfe/previa", pedido);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problema = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual("nfe.pedido_invalido", problema.GetProperty("code").GetString());
        var erros = problema.GetProperty("errors");
        Assert.IsTrue(erros.TryGetProperty("identificacao.naturezaOperacao", out _));
        Assert.IsTrue(erros.TryGetProperty("itens[0].produto.ncm", out _));
    }

    [TestMethod]
    public async Task Emitente_sem_certificado_ou_serie_inexistente_e_400()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant previa sem cert");
        using var client = _factory.CreateClient().AsTenant(apiKey);
        var emitente = await client.CriarEmitenteAsync(ApiClientExtensions.EmitentePf(CertificadoDeTeste.CpfValido));

        var semSerie = await client.PostAsJsonAsync($"/api/v1/emitentes/{emitente.Id}/nfe/previa", PedidosDeTeste.PedidoRegimeNormal(serie: 7));
        Assert.AreEqual(HttpStatusCode.BadRequest, semSerie.StatusCode);
        StringAssert.Contains(await semSerie.Content.ReadAsStringAsync(), "nfe.serie_nao_encontrada");

        await client.CriarSerieAsync(emitente.Id, 7, 1);
        var semCertificado = await client.PostAsJsonAsync($"/api/v1/emitentes/{emitente.Id}/nfe/previa", PedidosDeTeste.PedidoRegimeNormal(serie: 7));
        Assert.AreEqual(HttpStatusCode.BadRequest, semCertificado.StatusCode);
        StringAssert.Contains(await semCertificado.Content.ReadAsStringAsync(), "nfe.emitente_sem_certificado");
    }

    [TestMethod]
    public async Task Tenant_nao_gera_previa_para_emitente_de_outro_tenant()
    {
        var (_, chaveA) = await _factory.NovoTenantComChaveAsync("Tenant previa A");
        var (_, chaveB) = await _factory.NovoTenantComChaveAsync("Tenant previa B");
        using var clientA = _factory.CreateClient().AsTenant(chaveA);
        using var clientB = _factory.CreateClient().AsTenant(chaveB);
        var emitenteDeA = await clientA.EmitenteProntoAsync(ApiClientExtensions.EmitentePf(CertificadoDeTeste.CpfValido),
            $"X:{CertificadoDeTeste.CpfValido}", serie: 1, proximoNumero: 1);

        var response = await clientB.PostAsJsonAsync($"/api/v1/emitentes/{emitenteDeA.Id}/nfe/previa", PedidosDeTeste.PedidoRegimeNormal());

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
