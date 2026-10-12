using Emissor.Contracts.Emitentes;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Emitentes.Domain;
using Emissor.Modules.Emitentes.Tests;

namespace Emissor.Modules.Nfe.Tests;

/// <summary>
/// Pedidos de emissao prontos para os testes: um produtor rural PF no regime
/// normal e uma PJ no Simples Nacional, cada um com um pedido coerente
/// (impostos batendo, pagamento fechando com o total).
/// </summary>
public static class PedidosDeTeste
{
    public static readonly DateTimeOffset Agora = new(2026, 10, 11, 15, 30, 0, TimeSpan.Zero);

    public static Emitente EmitentePf(RegimeTributario regime = RegimeTributario.RegimeNormal, AmbienteSefaz ambiente = AmbienteSefaz.Homologacao) =>
        Emitente.Criar(Guid.NewGuid(), new Emitente.Dados(
            TipoEmitente.PessoaFisica,
            CertificadoDeTeste.CpfValido,
            "12345678",
            "Produtor Rural Teste",
            null,
            regime,
            ambiente,
            Endereco.Criar("Estrada do Produtor", "KM 12", null, "Zona Rural", "3304557", "Rio de Janeiro", "RJ", "20000000", "2122223333").Value!,
            "produtor@teste.com"), Agora).Value!;

    public static Emitente EmitentePj(RegimeTributario regime = RegimeTributario.SimplesNacional, AmbienteSefaz ambiente = AmbienteSefaz.Homologacao) =>
        Emitente.Criar(Guid.NewGuid(), new Emitente.Dados(
            TipoEmitente.PessoaJuridica,
            CertificadoDeTeste.CnpjValido,
            "87654321",
            "Empresa Teste LTDA",
            "Empresa Teste",
            regime,
            ambiente,
            Endereco.Criar("Rua das Flores", "100", "Sala 2", "Centro", "3304557", "Rio de Janeiro", "RJ", "20010000", null).Value!,
            null), Agora).Value!;

    public static DestinatarioDto DestinatarioPjContribuinte() => new(
        "11.222.333/0001-81",
        null,
        "Cliente Teste LTDA",
        IndicadorIeDestinatario.Contribuinte,
        "12345674",
        new EnderecoDto("Avenida Brasil", "1000", null, "Bonsucesso", "3304557", "Rio de Janeiro", "RJ", "21040000", null),
        "cliente@teste.com");

    public static DestinatarioDto DestinatarioPfNaoContribuinte() => new(
        "123.456.789-09",
        null,
        "Consumidor Teste",
        IndicadorIeDestinatario.NaoContribuinte,
        null,
        new EnderecoDto("Rua Um", "1", null, "Centro", "3550308", "São Paulo", "SP", "01001000", null),
        null);

    /// <summary>Item com ICMS 00 (18%), PIS/COFINS cumulativo e IBS/CBS em tributacao integral (CST 000).</summary>
    public static ItemNfeDto ItemRegimeNormal(decimal quantidade = 10m, decimal valorUnitario = 25m)
    {
        var total = decimal.Round(quantidade * valorUnitario, 2);

        return new ItemNfeDto(
            new ProdutoDto("CAFE-001", null, "Cafe arabica em grao", "09011110", null, 5101, "KG", quantidade, valorUnitario,
                null, null, null, null, null, null, null, null, null, null, null, null, null),
            new ImpostosItemDto(
                new IcmsDto(OrigemMercadoria.Nacional, "00", null, ModalidadeBaseCalculoIcms.ValorDaOperacao, total, null, 18m, decimal.Round(total * 0.18m, 2),
                    null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null),
                new PisDto("01", total, 0.65m, decimal.Round(total * 0.0065m, 2), null, null),
                new CofinsDto("01", total, 3m, decimal.Round(total * 0.03m, 2), null, null),
                null,
                new IbsCbsDto("000", "000001", total,
                    new TributoIbsCbsDto(0.1m, decimal.Round(total * 0.001m, 2), null, null, null, null, null),
                    new TributoIbsCbsDto(0m, 0m, null, null, null, null, null),
                    new TributoIbsCbsDto(0.9m, decimal.Round(total * 0.009m, 2), null, null, null, null, null),
                    null),
                decimal.Round(total * 0.25m, 2)),
            null);
    }

    /// <summary>Item do Simples Nacional: CSOSN 102, PIS/COFINS 49, IBS/CBS com CST 410 (nao incidencia, sem valores).</summary>
    public static ItemNfeDto ItemSimples(decimal quantidade = 2m, decimal valorUnitario = 150m) => new(
        new ProdutoDto("MESA-01", "7891234567895", "Mesa de madeira", "94036000", null, 5102, "UN", quantidade, valorUnitario,
            null, null, null, null, null, null, null, null, null, null, null, "PED-1", 1),
        new ImpostosItemDto(
            new IcmsDto(OrigemMercadoria.Nacional, null, "102",
                null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null),
            new PisDto("49", null, null, null, null, null),
            new CofinsDto("49", null, null, null, null, null),
            null,
            new IbsCbsDto("410", "410001", null, null, null, null, null),
            null),
        "Item de teste");

    public static EmissaoNfeRequest PedidoRegimeNormal(int serie = 1, DateTimeOffset? dataEmissao = null)
    {
        var item = ItemRegimeNormal();
        var total = item.Produto.Quantidade * item.Produto.ValorUnitario;

        return new EmissaoNfeRequest(
            new IdentificacaoNfeDto(serie, "Venda de producao do estabelecimento", TipoOperacaoNfe.Saida, FinalidadeNfe.Normal, false,
                PresencaComprador.Presencial, dataEmissao ?? Agora, null, null),
            DestinatarioPjContribuinte(),
            [item],
            new TransporteDto(ModalidadeFrete.PorContaDoDestinatario, null, [new VolumeDto(10, "SACA", null, null, 600m, 605m)]),
            new PagamentoDto([new DetalhePagamentoDto(IndicadorPagamento.APrazo, FormaPagamento.BoletoBancario, total, null)], null),
            new InformacoesAdicionaisDto(null, "Pedido de teste do emissor-nf."));
    }

    public static EmissaoNfeRequest PedidoSimples(int serie = 1, DateTimeOffset? dataEmissao = null)
    {
        var item = ItemSimples();
        var total = item.Produto.Quantidade * item.Produto.ValorUnitario;

        return new EmissaoNfeRequest(
            new IdentificacaoNfeDto(serie, "Venda de mercadoria", TipoOperacaoNfe.Saida, FinalidadeNfe.Normal, true,
                PresencaComprador.Internet, dataEmissao ?? Agora, null, null),
            DestinatarioPfNaoContribuinte(),
            [item],
            new TransporteDto(ModalidadeFrete.SemFrete, null, null),
            new PagamentoDto([new DetalhePagamentoDto(IndicadorPagamento.AVista, FormaPagamento.PixDinamico, total, null)], null),
            null);
    }
}
