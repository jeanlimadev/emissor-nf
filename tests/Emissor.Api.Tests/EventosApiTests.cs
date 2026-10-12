using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;
using Emissor.Contracts.Emitentes;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Emitentes.Tests;
using Emissor.Modules.Nfe.Tests;

namespace Emissor.Api.Tests;

[TestClass]
public sealed class EventosApiTests
{
    private static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/nfe";
    private static EmissorApiFactory _factory = null!;

    [ClassInitialize]
    public static void Up(TestContext _) => _factory = new EmissorApiFactory();

    [ClassCleanup]
    public static void Down() => _factory.Dispose();

    [TestMethod]
    public async Task Cancela_nota_autorizada_e_a_danfe_sai_marcada()
    {
        var (client, emitente) = await NovoEmitentePjAsync("cancela");
        var nota = await EmitirAutorizadaAsync(client, emitente.Id, "canc-1");

        var response = await client.PostAsJsonAsync($"/api/v1/nfe/{nota.Id}/cancelamento", new CancelamentoRequest("Erro no pedido do cliente, nota sera refeita."));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var evento = (await response.Content.ReadFromJsonAsync<EventoNfeResponse>())!;
        Assert.AreEqual(EventoStatus.Registrado, evento.Status);
        Assert.AreEqual(135, evento.CStat);
        Assert.IsNotNull(evento.Protocolo);

        var cancelada = await client.GetFromJsonAsync<NfeResponse>($"/api/v1/nfe/{nota.Id}");
        Assert.AreEqual(NfeStatus.Cancelada, cancelada!.Status);

        var xml = XDocument.Parse(await client.GetStringAsync($"/api/v1/nfe/{nota.Id}/eventos/{evento.Id}/xml")).Root!;
        Assert.AreEqual(Ns + "procEventoNFe", xml.Name);
        Assert.AreEqual("110111", xml.Descendants(Ns + "tpEvento").First().Value);

        var danfe = await client.GetAsync($"/api/v1/nfe/{nota.Id}/danfe");
        Assert.AreEqual(HttpStatusCode.OK, danfe.StatusCode, "cancelada ainda tem DANFE, com a marca");

        var deNovo = await client.PostAsJsonAsync($"/api/v1/nfe/{nota.Id}/cancelamento", new CancelamentoRequest("Erro no pedido do cliente, nota sera refeita."));
        Assert.AreEqual(HttpStatusCode.Conflict, deNovo.StatusCode);
        StringAssert.Contains(await deNovo.Content.ReadAsStringAsync(), "nfe.cancelamento_so_de_autorizada");

        var eventos = await client.GetFromJsonAsync<List<EventoNfeResponse>>($"/api/v1/nfe/{nota.Id}/eventos");
        Assert.AreEqual(1, eventos!.Count);
    }

    [TestMethod]
    public async Task Cancelamento_rejeitado_pela_sefaz_mantem_a_nota_autorizada()
    {
        var (client, emitente) = await NovoEmitentePjAsync("canc-rej");
        var nota = await EmitirAutorizadaAsync(client, emitente.Id, "canc-rej-1");

        var response = await client.PostAsJsonAsync($"/api/v1/nfe/{nota.Id}/cancelamento", new CancelamentoRequest("REJEITAR este cancelamento no teste."));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var evento = (await response.Content.ReadFromJsonAsync<EventoNfeResponse>())!;
        Assert.AreEqual(EventoStatus.Rejeitado, evento.Status);
        Assert.AreEqual(573, evento.CStat);
        Assert.AreEqual(NfeStatus.Autorizada, (await client.GetFromJsonAsync<NfeResponse>($"/api/v1/nfe/{nota.Id}"))!.Status);
    }

    [TestMethod]
    public async Task Sem_resposta_o_evento_fica_pendente_e_a_repeticao_conclui_sem_duplicar()
    {
        var (client, emitente) = await NovoEmitentePjAsync("canc-pend");
        var nota = await EmitirAutorizadaAsync(client, emitente.Id, "canc-pend-1");
        var pedido = new CancelamentoRequest("SEM-RESPOSTA na primeira vez, teste.");

        var primeira = await client.PostAsJsonAsync($"/api/v1/nfe/{nota.Id}/cancelamento", pedido);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, primeira.StatusCode);
        var pendentes = await client.GetFromJsonAsync<List<EventoNfeResponse>>($"/api/v1/nfe/{nota.Id}/eventos");
        Assert.AreEqual(EventoStatus.Pendente, pendentes!.Single().Status);

        var segunda = await client.PostAsJsonAsync($"/api/v1/nfe/{nota.Id}/cancelamento", pedido);
        Assert.AreEqual(HttpStatusCode.OK, segunda.StatusCode, await segunda.Content.ReadAsStringAsync());
        var evento = (await segunda.Content.ReadFromJsonAsync<EventoNfeResponse>())!;
        Assert.AreEqual(EventoStatus.Registrado, evento.Status);
        Assert.AreEqual(pendentes!.Single().Id, evento.Id, "o mesmo evento, nao um segundo");
        Assert.AreEqual(2, FakeSefazGateway.EventosEnviados[$"{nota.ChaveAcesso}:110111:1"]);
    }

    [TestMethod]
    public async Task Cartas_de_correcao_em_sequencia_e_limite()
    {
        var (client, emitente) = await NovoEmitentePjAsync("cce");
        var nota = await EmitirAutorizadaAsync(client, emitente.Id, "cce-1");

        var primeira = (await (await client.PostAsJsonAsync($"/api/v1/nfe/{nota.Id}/carta-correcao", new CartaCorrecaoRequest("Corrige o endereco de entrega para a rua certa."))).Content.ReadFromJsonAsync<EventoNfeResponse>())!;
        var segunda = (await (await client.PostAsJsonAsync($"/api/v1/nfe/{nota.Id}/carta-correcao", new CartaCorrecaoRequest("Corrige tambem o complemento do endereco."))).Content.ReadFromJsonAsync<EventoNfeResponse>())!;

        Assert.AreEqual(1, primeira.Sequencia);
        Assert.AreEqual(2, segunda.Sequencia);
        Assert.AreEqual(EventoStatus.Registrado, segunda.Status);
        Assert.AreEqual(NfeStatus.Autorizada, (await client.GetFromJsonAsync<NfeResponse>($"/api/v1/nfe/{nota.Id}"))!.Status, "carta nao muda o status da nota");

        var curta = await client.PostAsJsonAsync($"/api/v1/nfe/{nota.Id}/carta-correcao", new CartaCorrecaoRequest("curta"));
        Assert.AreEqual(HttpStatusCode.BadRequest, curta.StatusCode);
        StringAssert.Contains(await curta.Content.ReadAsStringAsync(), "nfe.correcao_invalida");
    }

    [TestMethod]
    public async Task Inutiliza_faixa_nunca_usada_e_avanca_a_serie()
    {
        var (client, emitente) = await NovoEmitentePjAsync("inut", proximoNumero: 1);
        var nota = await EmitirAutorizadaAsync(client, emitente.Id, "inut-1"); // consome o 1

        var ocupada = await client.PostAsJsonAsync($"/api/v1/emitentes/{emitente.Id}/inutilizacoes", new InutilizacaoRequest(1, 1, 3, "Numeros pulados por falha do sistema."));
        Assert.AreEqual(HttpStatusCode.Conflict, ocupada.StatusCode);
        StringAssert.Contains(await ocupada.Content.ReadAsStringAsync(), "nfe.faixa_com_nota_emitida");

        var response = await client.PostAsJsonAsync($"/api/v1/emitentes/{emitente.Id}/inutilizacoes", new InutilizacaoRequest(1, 2, 5, "Numeros pulados por falha do sistema."));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var inut = (await response.Content.ReadFromJsonAsync<InutilizacaoResponse>())!;
        Assert.AreEqual(EventoStatus.Registrado, inut.Status);
        Assert.AreEqual(102, inut.CStat);

        var series = await client.GetFromJsonAsync<List<SerieResponse>>($"/api/v1/emitentes/{emitente.Id}/series");
        Assert.AreEqual(6, series!.Single().ProximoNumero, "a serie pula a faixa inutilizada");

        var xml = XDocument.Parse(await client.GetStringAsync($"/api/v1/emitentes/{emitente.Id}/inutilizacoes/{inut.Id}/xml")).Root!;
        Assert.AreEqual(Ns + "procInutNFe", xml.Name);

        var lista = await client.GetFromJsonAsync<List<InutilizacaoResponse>>($"/api/v1/emitentes/{emitente.Id}/inutilizacoes");
        Assert.AreEqual(1, lista!.Count);
        _ = nota;
    }

    [TestMethod]
    public async Task Emitente_cpf_nao_inutiliza()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant inut pf");
        using var client = _factory.CreateClient().AsTenant(apiKey);
        var emitente = await client.EmitenteProntoAsync(ApiClientExtensions.EmitentePf(CertificadoDeTeste.CpfValido), $"X:{CertificadoDeTeste.CpfValido}", 1, 1);

        var response = await client.PostAsJsonAsync($"/api/v1/emitentes/{emitente.Id}/inutilizacoes", new InutilizacaoRequest(1, 1, 1, "Numeros pulados por falha do sistema."));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "nfe.inutilizacao_so_para_cnpj");
    }

    // ----- apoio ---------------------------------------------------------------------------------

    private static async Task<(HttpClient Client, EmitenteResponse Emitente)> NovoEmitentePjAsync(string nome, long proximoNumero = 1)
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant eventos " + nome);
        var client = _factory.CreateClient().AsTenant(apiKey);
        var pj = new EmitenteRequest(TipoEmitente.PessoaJuridica, CertificadoDeTeste.CnpjValido, "87654321", "Empresa Teste LTDA", null,
            RegimeTributario.SimplesNacional, AmbienteSefaz.Homologacao,
            new EnderecoDto("Rua das Flores", "100", null, "Centro", "3304557", "Rio de Janeiro", "RJ", "20010000", null), null);
        var emitente = await client.EmitenteProntoAsync(pj, $"EMPRESA TESTE:{CertificadoDeTeste.CnpjValido}", serie: 1, proximoNumero: proximoNumero);

        return (client, emitente);
    }

    private static async Task<NfeResponse> EmitirAutorizadaAsync(HttpClient client, Guid emitenteId, string chave)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/emitentes/{emitenteId}/nfe?aguardar=30") { Content = JsonContent.Create(PedidosDeTeste.PedidoSimples()) };
        request.Headers.Add("Idempotency-Key", chave);
        var nota = (await (await client.SendAsync(request)).Content.ReadFromJsonAsync<NfeResponse>())!;
        Assert.AreEqual(NfeStatus.Autorizada, nota.Status, nota.XMotivo);
        return nota;
    }
}
