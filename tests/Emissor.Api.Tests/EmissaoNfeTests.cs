using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;
using Emissor.Contracts.Emitentes;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Emitentes.Tests;
using Emissor.Modules.Nfe.Tests;

namespace Emissor.Api.Tests;

/// <summary>Emissao de ponta a ponta com a SEFAZ falsa: fila, worker, numeracao, idempotencia e os caminhos de retorno.</summary>
[TestClass]
public sealed class EmissaoNfeTests
{
    private const int Aguardar = 30;
    private static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/nfe";
    private static EmissorApiFactory _factory = null!;

    [ClassInitialize]
    public static void Up(TestContext _) => _factory = new EmissorApiFactory();

    [ClassCleanup]
    public static void Down() => _factory.Dispose();

    [TestMethod]
    public async Task Emite_e_autoriza_com_protocolo_numero_e_procnfe()
    {
        var (client, emitente) = await NovoEmitenteAsync("autoriza", proximoNumero: 10);

        var response = await Emitir(client, emitente.Id, "pedido-1", PedidosDeTeste.PedidoRegimeNormal(), Aguardar);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var nota = (await response.Content.ReadFromJsonAsync<NfeResponse>())!;
        Assert.AreEqual(NfeStatus.Autorizada, nota.Status, nota.XMotivo);
        Assert.AreEqual(10, nota.Numero);
        Assert.AreEqual(100, nota.CStat);
        Assert.IsNotNull(nota.Protocolo);
        Assert.AreEqual(44, nota.ChaveAcesso!.Length);

        var xml = await client.GetStringAsync($"/api/v1/nfe/{nota.Id}/xml");
        var proc = XDocument.Parse(xml).Root!;
        Assert.AreEqual(Ns + "nfeProc", proc.Name);
        Assert.AreEqual(nota.Protocolo, proc.Element(Ns + "protNFe")!.Element(Ns + "infProt")!.Element(Ns + "nProt")!.Value);
        Assert.AreEqual("NFe" + nota.ChaveAcesso, proc.Element(Ns + "NFe")!.Element(Ns + "infNFe")!.Attribute("Id")!.Value);

        var series = await client.GetFromJsonAsync<List<SerieResponse>>($"/api/v1/emitentes/{emitente.Id}/series");
        Assert.AreEqual(11, series!.Single().ProximoNumero, "o numero foi consumido");

        var lista = await client.GetFromJsonAsync<Emissor.BuildingBlocks.PagedResponse<NfeResponse>>($"/api/v1/nfe?emitenteId={emitente.Id}&status=Autorizada");
        Assert.AreEqual(1, lista!.Total);
    }

    [TestMethod]
    public async Task Danfe_so_depois_de_autorizada_e_previa_em_pdf_sempre()
    {
        var (client, emitente) = await NovoEmitenteAsync("danfe");

        var previa = await client.PostAsJsonAsync($"/api/v1/emitentes/{emitente.Id}/nfe/previa/danfe", PedidosDeTeste.PedidoRegimeNormal());
        Assert.AreEqual(HttpStatusCode.OK, previa.StatusCode, await previa.Content.ReadAsStringAsync());
        Assert.AreEqual("application/pdf", previa.Content.Headers.ContentType!.MediaType);
        StringAssert.StartsWith(System.Text.Encoding.ASCII.GetString((await previa.Content.ReadAsByteArrayAsync())[..4]), "%PDF");

        var nota = (await (await Emitir(client, emitente.Id, "danfe-1", PedidosDeTeste.PedidoRegimeNormal(), Aguardar)).Content.ReadFromJsonAsync<NfeResponse>())!;
        Assert.AreEqual(NfeStatus.Autorizada, nota.Status, nota.XMotivo);

        var danfe = await client.GetAsync($"/api/v1/nfe/{nota.Id}/danfe");
        Assert.AreEqual(HttpStatusCode.OK, danfe.StatusCode, await danfe.Content.ReadAsStringAsync());
        Assert.AreEqual("application/pdf", danfe.Content.Headers.ContentType!.MediaType);
        Assert.IsTrue((await danfe.Content.ReadAsByteArrayAsync()).Length > 5_000);

        var rejeitada = (await (await Emitir(client, emitente.Id, "danfe-2", ComCodigoDoPrimeiroProduto(PedidosDeTeste.PedidoRegimeNormal(), "REJEITAR"), Aguardar)).Content.ReadFromJsonAsync<NfeResponse>())!;
        var semDanfe = await client.GetAsync($"/api/v1/nfe/{rejeitada.Id}/danfe");
        Assert.AreEqual(HttpStatusCode.Conflict, semDanfe.StatusCode);
        StringAssert.Contains(await semDanfe.Content.ReadAsStringAsync(), "nfe.danfe_indisponivel");
    }

    [TestMethod]
    public async Task Sem_aguardar_responde_202_e_a_nota_fica_na_fila()
    {
        var (client, emitente) = await NovoEmitenteAsync("fila");

        var response = await Emitir(client, emitente.Id, "pedido-fila", PedidosDeTeste.PedidoRegimeNormal(), aguardar: null);

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());
        var nota = (await response.Content.ReadFromJsonAsync<NfeResponse>())!;
        Assert.IsTrue(nota.Status is NfeStatus.Recebida or NfeStatus.EmProcessamento or NfeStatus.Autorizada);
        Assert.AreEqual($"/api/v1/nfe/{nota.Id}", response.Headers.Location!.ToString());
    }

    [TestMethod]
    public async Task Mesma_idempotency_key_devolve_a_mesma_nota_e_pedido_diferente_e_409()
    {
        var (client, emitente) = await NovoEmitenteAsync("idem");
        var pedido = PedidosDeTeste.PedidoRegimeNormal();

        var primeira = (await (await Emitir(client, emitente.Id, "chave-x", pedido, null)).Content.ReadFromJsonAsync<NfeResponse>())!;
        var repetida = (await (await Emitir(client, emitente.Id, "chave-x", pedido, null)).Content.ReadFromJsonAsync<NfeResponse>())!;
        Assert.AreEqual(primeira.Id, repetida.Id);

        var diferente = await Emitir(client, emitente.Id, "chave-x", pedido with { Identificacao = pedido.Identificacao with { NaturezaOperacao = "Outra" } }, null);
        Assert.AreEqual(HttpStatusCode.Conflict, diferente.StatusCode);
        StringAssert.Contains(await diferente.Content.ReadAsStringAsync(), "nfe.idempotencia_conflito");

        var semChave = await client.PostAsJsonAsync($"/api/v1/emitentes/{emitente.Id}/nfe", pedido);
        Assert.AreEqual(HttpStatusCode.BadRequest, semChave.StatusCode);
        StringAssert.Contains(await semChave.Content.ReadAsStringAsync(), "nfe.idempotencia_ausente");
    }

    [TestMethod]
    public async Task Rejeicao_vira_rejeitada_e_o_reenvio_corrigido_mantem_o_numero()
    {
        var (client, emitente) = await NovoEmitenteAsync("rejeita", proximoNumero: 5);
        var pedido = ComCodigoDoPrimeiroProduto(PedidosDeTeste.PedidoRegimeNormal(), "REJEITAR");

        var rejeitada = (await (await Emitir(client, emitente.Id, "rej-1", pedido, Aguardar)).Content.ReadFromJsonAsync<NfeResponse>())!;
        Assert.AreEqual(NfeStatus.Rejeitada, rejeitada.Status);
        Assert.AreEqual(778, rejeitada.CStat);
        StringAssert.Contains(rejeitada.XMotivo, "NCM");
        Assert.AreEqual(5, rejeitada.Numero);

        var corrigido = ComCodigoDoPrimeiroProduto(pedido, "CAFE-001");
        var reenvio = await client.PostAsJsonAsync($"/api/v1/nfe/{rejeitada.Id}/reenviar", corrigido);
        Assert.AreEqual(HttpStatusCode.Accepted, reenvio.StatusCode, await reenvio.Content.ReadAsStringAsync());

        var final = await AguardarFinalAsync(client, rejeitada.Id);
        Assert.AreEqual(NfeStatus.Autorizada, final.Status, final.XMotivo);
        Assert.AreEqual(5, final.Numero, "o reenvio nao consome numero novo");
        Assert.AreNotEqual(rejeitada.ChaveAcesso, final.ChaveAcesso, "a chave e refeita (cNF novo)");

        var series = await client.GetFromJsonAsync<List<SerieResponse>>($"/api/v1/emitentes/{emitente.Id}/series");
        Assert.AreEqual(6, series!.Single().ProximoNumero);
    }

    [TestMethod]
    public async Task Sem_resposta_da_sefaz_consulta_pela_chave_antes_de_reenviar()
    {
        var (client, emitente) = await NovoEmitenteAsync("timeout");
        var pedido = ComCodigoDoPrimeiroProduto(PedidosDeTeste.PedidoRegimeNormal(), "SEM-RESPOSTA");

        var nota = (await (await Emitir(client, emitente.Id, "to-1", pedido, Aguardar)).Content.ReadFromJsonAsync<NfeResponse>())!;

        Assert.AreEqual(NfeStatus.Autorizada, nota.Status, nota.XMotivo);
        Assert.AreEqual(1, FakeSefazGateway.Transmissoes[nota.ChaveAcesso!], "a nota entrou na primeira vez: nao pode ser transmitida de novo");
        Assert.IsTrue(FakeSefazGateway.Consultas[nota.ChaveAcesso!] >= 1, "a consulta pela chave e que resolveu");
        Assert.IsTrue(nota.Tentativas >= 2);
    }

    [TestMethod]
    public async Task Lote_so_recebido_fica_pendente_e_a_consulta_autoriza()
    {
        var (client, emitente) = await NovoEmitenteAsync("lote103");
        var pedido = ComCodigoDoPrimeiroProduto(PedidosDeTeste.PedidoRegimeNormal(), "LOTE-103");

        var nota = (await (await Emitir(client, emitente.Id, "lote-1", pedido, Aguardar)).Content.ReadFromJsonAsync<NfeResponse>())!;

        Assert.AreEqual(NfeStatus.Autorizada, nota.Status, nota.XMotivo);
        Assert.AreEqual(1, FakeSefazGateway.Transmissoes[nota.ChaveAcesso!]);
        Assert.IsTrue(FakeSefazGateway.ConsultasDeRecibo.Values.Any(v => v >= 2), "o recibo foi consultado ate o lote processar");
        Assert.IsFalse(FakeSefazGateway.Consultas.ContainsKey(nota.ChaveAcesso!), "com recibo, nao consulta pela chave");
    }

    [TestMethod]
    public async Task Emissoes_simultaneas_recebem_numeros_distintos_e_sequenciais()
    {
        var (client, emitente) = await NovoEmitenteAsync("paralelo", proximoNumero: 100);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            Emitir(client, emitente.Id, $"par-{i}", PedidosDeTeste.PedidoRegimeNormal(), aguardar: null)));
        var ids = new List<Guid>();

        foreach (var r in respostas)
            ids.Add((await r.Content.ReadFromJsonAsync<NfeResponse>())!.Id);

        var finais = new List<NfeResponse>();

        foreach (var id in ids)
            finais.Add(await AguardarFinalAsync(client, id));

        Assert.IsTrue(finais.All(n => n.Status == NfeStatus.Autorizada), string.Join(" | ", finais.Select(n => n.XMotivo)));
        CollectionAssert.AreEquivalent(Enumerable.Range(100, 6).Select(n => (long)n).ToList(), finais.Select(n => n.Numero!.Value).ToList());
    }

    [TestMethod]
    public async Task Status_do_servico_usa_o_certificado_do_emitente()
    {
        var (client, emitente) = await NovoEmitenteAsync("status");

        var status = await client.GetFromJsonAsync<StatusServicoSefazResponse>($"/api/v1/emitentes/{emitente.Id}/sefaz/status");

        Assert.AreEqual(107, status!.CStat);
        Assert.AreEqual("SVRS", status.Autorizador);
        Assert.AreEqual(AmbienteSefaz.Homologacao, status.Ambiente);
    }

    [TestMethod]
    public async Task Tenant_nao_enxerga_nota_de_outro_tenant()
    {
        var (clientA, emitente) = await NovoEmitenteAsync("iso-a");
        var (_, chaveB) = await _factory.NovoTenantComChaveAsync("Tenant nfe iso-b");
        using var clientB = _factory.CreateClient().AsTenant(chaveB);

        var nota = (await (await Emitir(clientA, emitente.Id, "iso-1", PedidosDeTeste.PedidoRegimeNormal(), null)).Content.ReadFromJsonAsync<NfeResponse>())!;

        Assert.AreEqual(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/v1/nfe/{nota.Id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/v1/nfe/{nota.Id}/xml")).StatusCode);
        var lista = await clientB.GetFromJsonAsync<Emissor.BuildingBlocks.PagedResponse<NfeResponse>>("/api/v1/nfe");
        Assert.AreEqual(0, lista!.Total);
    }

    // ----- apoio ---------------------------------------------------------------------------------

    private static async Task<(HttpClient Client, EmitenteResponse Emitente)> NovoEmitenteAsync(string nome, long proximoNumero = 1)
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant nfe " + nome);
        var client = _factory.CreateClient().AsTenant(apiKey);
        var emitente = await client.EmitenteProntoAsync(ApiClientExtensions.EmitentePf(CertificadoDeTeste.CpfValido),
            $"PRODUTOR RURAL TESTE:{CertificadoDeTeste.CpfValido}", serie: 1, proximoNumero: proximoNumero);

        return (client, emitente);
    }

    private static Task<HttpResponseMessage> Emitir(HttpClient client, Guid emitenteId, string idempotencyKey, EmissaoNfeRequest pedido, int? aguardar)
    {
        var url = $"/api/v1/emitentes/{emitenteId}/nfe" + (aguardar is null ? string.Empty : $"?aguardar={aguardar}");
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(pedido) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }

    private static async Task<NfeResponse> AguardarFinalAsync(HttpClient client, Guid id)
    {
        var limite = DateTimeOffset.UtcNow.AddSeconds(Aguardar);

        while (true)
        {
            var nota = (await client.GetFromJsonAsync<NfeResponse>($"/api/v1/nfe/{id}"))!;

            if (nota.Status is NfeStatus.Autorizada or NfeStatus.Rejeitada or NfeStatus.Denegada or NfeStatus.Falha || DateTimeOffset.UtcNow > limite)
                return nota;

            await Task.Delay(250);
        }
    }

    private static EmissaoNfeRequest ComCodigoDoPrimeiroProduto(EmissaoNfeRequest pedido, string codigo)
    {
        var item = pedido.Itens[0];
        return pedido with { Itens = [item with { Produto = item.Produto with { Codigo = codigo } }, .. pedido.Itens.Skip(1)] };
    }
}
