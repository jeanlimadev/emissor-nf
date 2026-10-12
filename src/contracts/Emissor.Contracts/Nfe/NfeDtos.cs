using Emissor.Contracts.Emitentes;

namespace Emissor.Contracts.Nfe;

// O pedido de emissao espelha a NF-e com nomes do dominio em portugues. Os
// valores numericos dos enums sao exatamente os codigos do layout 4.00, para o
// sistema cliente que ja conhece a NF-e nao precisar de tabela de conversao.

/// <summary>Campo <c>tpNF</c>: 0 entrada, 1 saida.</summary>
public enum TipoOperacaoNfe
{
    Entrada = 0,
    Saida = 1
}

/// <summary>Campo <c>finNFe</c>.</summary>
public enum FinalidadeNfe
{
    Normal = 1,
    Complementar = 2,
    Ajuste = 3,
    Devolucao = 4
}

/// <summary>Campo <c>indPres</c>: como o comprador estava presente na operacao.</summary>
public enum PresencaComprador
{
    NaoSeAplica = 0,
    Presencial = 1,
    Internet = 2,
    Teleatendimento = 3,
    EntregaDomicilio = 4,
    PresencialForaDoEstabelecimento = 5,
    Outros = 9
}

/// <summary>Campo <c>indIEDest</c>.</summary>
public enum IndicadorIeDestinatario
{
    Contribuinte = 1,
    Isento = 2,
    NaoContribuinte = 9
}

/// <summary>Campo <c>orig</c> do ICMS.</summary>
public enum OrigemMercadoria
{
    Nacional = 0,
    EstrangeiraImportacaoDireta = 1,
    EstrangeiraAdquiridaNoBrasil = 2,
    NacionalConteudoImportacaoAcimaDe40 = 3,
    NacionalProcessosProdutivosBasicos = 4,
    NacionalConteudoImportacaoAte40 = 5,
    EstrangeiraImportacaoDiretaSemSimilar = 6,
    EstrangeiraAdquiridaNoBrasilSemSimilar = 7,
    NacionalConteudoImportacaoAcimaDe70 = 8
}

/// <summary>Campo <c>modBC</c> do ICMS.</summary>
public enum ModalidadeBaseCalculoIcms
{
    MargemValorAgregado = 0,
    Pauta = 1,
    PrecoTabelado = 2,
    ValorDaOperacao = 3
}

/// <summary>Campo <c>modBCST</c> do ICMS ST.</summary>
public enum ModalidadeBaseCalculoIcmsSt
{
    PrecoTabelado = 0,
    ListaNegativa = 1,
    ListaPositiva = 2,
    ListaNeutra = 3,
    MargemValorAgregado = 4,
    Pauta = 5,
    ValorDaOperacao = 6
}

/// <summary>Campo <c>modFrete</c>.</summary>
public enum ModalidadeFrete
{
    PorContaDoEmitente = 0,
    PorContaDoDestinatario = 1,
    PorContaDeTerceiros = 2,
    ProprioPorContaDoRemetente = 3,
    ProprioPorContaDoDestinatario = 4,
    SemFrete = 9
}

/// <summary>Campo <c>indPag</c> do detalhe de pagamento.</summary>
public enum IndicadorPagamento
{
    AVista = 0,
    APrazo = 1
}

/// <summary>Campo <c>tPag</c>.</summary>
public enum FormaPagamento
{
    Dinheiro = 1,
    Cheque = 2,
    CartaoDeCredito = 3,
    CartaoDeDebito = 4,
    CartaoDaLoja = 5,
    ValeAlimentacao = 10,
    ValeRefeicao = 11,
    ValePresente = 12,
    ValeCombustivel = 13,
    DuplicataMercantil = 14,
    BoletoBancario = 15,
    DepositoBancario = 16,
    PixDinamico = 17,
    TransferenciaBancaria = 18,
    ProgramaDeFidelidade = 19,
    PixEstatico = 20,
    CreditoEmLoja = 21,
    PagamentoEletronicoNaoInformado = 22,
    PixAutomatico = 23,
    SemPagamento = 90,
    PagamentoPosterior = 91,
    Outros = 99
}

/// <summary>Pedido de emissao de uma NF-e (modelo 55). Impostos chegam calculados pelo sistema cliente.</summary>
public sealed record EmissaoNfeRequest(
    IdentificacaoNfeDto Identificacao,
    DestinatarioDto Destinatario,
    IReadOnlyList<ItemNfeDto> Itens,
    TransporteDto Transporte,
    PagamentoDto Pagamento,
    InformacoesAdicionaisDto? InformacoesAdicionais);

/// <param name="Serie">Serie cadastrada no emitente; o numero vem da serie.</param>
/// <param name="NaturezaOperacao">Campo <c>natOp</c>, ate 60 caracteres (ex.: "Venda de producao do estabelecimento").</param>
/// <param name="DataEmissao">Campo <c>dhEmi</c>. Vazio usa o relogio do servico.</param>
/// <param name="DataSaidaEntrada">Campo <c>dhSaiEnt</c>, opcional.</param>
/// <param name="NotasReferenciadas">Chaves de acesso (44 digitos) de NF-e referenciadas, ex.: em devolucao.</param>
public sealed record IdentificacaoNfeDto(
    int Serie,
    string NaturezaOperacao,
    TipoOperacaoNfe TipoOperacao,
    FinalidadeNfe Finalidade,
    bool ConsumidorFinal,
    PresencaComprador Presenca,
    DateTimeOffset? DataEmissao,
    DateTimeOffset? DataSaidaEntrada,
    IReadOnlyList<string>? NotasReferenciadas);

/// <param name="Documento">CPF ou CNPJ do destinatario. Para estrangeiro, deixe vazio e preencha <paramref name="IdentificacaoEstrangeiro"/>.</param>
/// <param name="InscricaoEstadual">Obrigatoria quando <paramref name="IndicadorIe"/> e Contribuinte; proibida nos demais.</param>
public sealed record DestinatarioDto(
    string? Documento,
    string? IdentificacaoEstrangeiro,
    string Nome,
    IndicadorIeDestinatario IndicadorIe,
    string? InscricaoEstadual,
    EnderecoDto Endereco,
    string? Email);

public sealed record ItemNfeDto(
    ProdutoDto Produto,
    ImpostosItemDto Impostos,
    string? InformacoesAdicionais);

/// <param name="CodigoBarras">GTIN (<c>cEAN</c>). Vazio vira "SEM GTIN".</param>
/// <param name="ValorTotal">Campo <c>vProd</c>. Vazio calcula quantidade x valor unitario (2 casas).</param>
/// <param name="CompoeTotal">Campo <c>indTot</c>. Vazio vale "sim".</param>
/// <param name="CodigoBeneficioFiscal">Campo <c>cBenef</c>, exigido por algumas UFs em operacoes com beneficio.</param>
public sealed record ProdutoDto(
    string Codigo,
    string? CodigoBarras,
    string Descricao,
    string Ncm,
    string? Cest,
    int Cfop,
    string Unidade,
    decimal Quantidade,
    decimal ValorUnitario,
    decimal? ValorTotal,
    decimal? Frete,
    decimal? Seguro,
    decimal? Desconto,
    decimal? OutrasDespesas,
    string? UnidadeTributavel,
    decimal? QuantidadeTributavel,
    decimal? ValorUnitarioTributavel,
    string? CodigoBarrasTributavel,
    bool? CompoeTotal,
    string? CodigoBeneficioFiscal,
    string? NumeroPedido,
    int? ItemPedido);

/// <param name="IbsCbs">Grupo IBS/CBS (NT 2025.002). Obrigatorio para quem ja esta na Reforma Tributaria.</param>
/// <param name="ValorAproximadoTributos">Campo <c>vTotTrib</c> (Lei da transparencia), opcional.</param>
public sealed record ImpostosItemDto(
    IcmsDto Icms,
    PisDto Pis,
    CofinsDto Cofins,
    IpiDto? Ipi,
    IbsCbsDto? IbsCbs,
    decimal? ValorAproximadoTributos);

/// <summary>
/// ICMS do item. Regime normal usa <paramref name="Cst"/> (00, 10, 20, 30, 40, 41, 50, 51, 60, 70, 90);
/// Simples Nacional usa <paramref name="Csosn"/> (101, 102, 103, 201, 202, 203, 300, 400, 500, 900).
/// Cada CST/CSOSN exige um subconjunto dos campos; o que faltar e apontado por nome.
/// </summary>
public sealed record IcmsDto(
    OrigemMercadoria Origem,
    string? Cst,
    string? Csosn,
    ModalidadeBaseCalculoIcms? ModalidadeBaseCalculo,
    decimal? BaseCalculo,
    decimal? PercentualReducaoBase,
    decimal? Aliquota,
    decimal? Valor,
    decimal? BaseCalculoFcp,
    decimal? PercentualFcp,
    decimal? ValorFcp,
    ModalidadeBaseCalculoIcmsSt? ModalidadeBaseCalculoSt,
    decimal? PercentualMvaSt,
    decimal? PercentualReducaoBaseSt,
    decimal? BaseCalculoSt,
    decimal? AliquotaSt,
    decimal? ValorSt,
    decimal? BaseCalculoFcpSt,
    decimal? PercentualFcpSt,
    decimal? ValorFcpSt,
    decimal? BaseCalculoStRetido,
    decimal? AliquotaSuportadaConsumidorFinal,
    decimal? ValorIcmsSubstituto,
    decimal? ValorStRetido,
    decimal? BaseCalculoFcpStRetido,
    decimal? PercentualFcpStRetido,
    decimal? ValorFcpStRetido,
    decimal? ValorDesonerado,
    int? MotivoDesoneracao,
    bool? DeduzDesoneracaoDoTotal,
    decimal? ValorOperacao,
    decimal? PercentualDiferimento,
    decimal? ValorDiferido,
    decimal? PercentualCreditoSimples,
    decimal? ValorCreditoSimples);

/// <summary>PIS do item. CST 01/02 por aliquota, 03 por quantidade, 04 a 09 sem incidencia, 49 a 99 outras.</summary>
public sealed record PisDto(
    string Cst,
    decimal? BaseCalculo,
    decimal? Aliquota,
    decimal? Valor,
    decimal? QuantidadeVendida,
    decimal? AliquotaEmReais);

/// <summary>COFINS do item, mesma estrutura do PIS.</summary>
public sealed record CofinsDto(
    string Cst,
    decimal? BaseCalculo,
    decimal? Aliquota,
    decimal? Valor,
    decimal? QuantidadeVendida,
    decimal? AliquotaEmReais);

/// <param name="CodigoEnquadramento">Campo <c>cEnq</c>. Vazio usa 999.</param>
public sealed record IpiDto(
    string Cst,
    string? CodigoEnquadramento,
    decimal? BaseCalculo,
    decimal? Aliquota,
    decimal? Valor);

/// <summary>
/// Grupo <c>IBSCBS</c> do item. <paramref name="Cst"/> e <paramref name="ClassificacaoTributaria"/>
/// (<c>cClassTrib</c>) sao sempre obrigatorios. Os valores (<c>gIBSCBS</c>) vao juntos:
/// informando base de calculo ou qualquer tributo, os quatro passam a ser exigidos.
/// </summary>
public sealed record IbsCbsDto(
    string Cst,
    string ClassificacaoTributaria,
    decimal? BaseCalculo,
    TributoIbsCbsDto? IbsUf,
    TributoIbsCbsDto? IbsMunicipal,
    TributoIbsCbsDto? Cbs,
    TributacaoRegularDto? TributacaoRegular);

/// <param name="Aliquota">Campo <c>pIBSUF</c>/<c>pIBSMun</c>/<c>pCBS</c>.</param>
/// <param name="Valor">Campo <c>vIBSUF</c>/<c>vIBSMun</c>/<c>vCBS</c>.</param>
public sealed record TributoIbsCbsDto(
    decimal Aliquota,
    decimal Valor,
    decimal? PercentualDiferimento,
    decimal? ValorDiferimento,
    decimal? ValorDevolucaoTributo,
    decimal? PercentualReducaoAliquota,
    decimal? AliquotaEfetiva);

/// <summary>Grupo <c>gTribRegular</c>: como seria a tributacao sem o beneficio aplicado no item.</summary>
public sealed record TributacaoRegularDto(
    string Cst,
    string ClassificacaoTributaria,
    decimal AliquotaEfetivaIbsUf,
    decimal ValorIbsUf,
    decimal AliquotaEfetivaIbsMunicipal,
    decimal ValorIbsMunicipal,
    decimal AliquotaEfetivaCbs,
    decimal ValorCbs);

public sealed record TransporteDto(
    ModalidadeFrete ModalidadeFrete,
    TransportadorDto? Transportador,
    IReadOnlyList<VolumeDto>? Volumes);

public sealed record TransportadorDto(
    string? Documento,
    string? Nome,
    string? InscricaoEstadual,
    string? EnderecoCompleto,
    string? Municipio,
    string? Uf);

public sealed record VolumeDto(
    int? Quantidade,
    string? Especie,
    string? Marca,
    string? Numeracao,
    decimal? PesoLiquido,
    decimal? PesoBruto);

/// <summary>Grupo <c>pag</c>. A soma dos detalhes (menos o troco) tem que fechar com o total da nota.</summary>
public sealed record PagamentoDto(
    IReadOnlyList<DetalhePagamentoDto> Detalhes,
    decimal? Troco);

/// <param name="Descricao">Campo <c>xPag</c>, obrigatorio quando a forma e Outros.</param>
public sealed record DetalhePagamentoDto(
    IndicadorPagamento? Indicador,
    FormaPagamento Forma,
    decimal Valor,
    string? Descricao);

/// <param name="Fisco">Campo <c>infAdFisco</c>.</param>
/// <param name="Contribuinte">Campo <c>infCpl</c>.</param>
public sealed record InformacoesAdicionaisDto(
    string? Fisco,
    string? Contribuinte);

/// <summary>
/// Resultado da previa: a NF-e montada, validada contra o schema e assinada,
/// sem transmitir e sem consumir numero. Serve para o cliente conferir o XML.
/// </summary>
public sealed record NfePreviaResponse(
    string ChaveAcesso,
    int Serie,
    long Numero,
    DateTimeOffset DataEmissao,
    string Xml);

/// <summary>
/// Ciclo de vida da nota no emissor. Recebida e PendenteConsulta ainda vao
/// mudar; Autorizada, Rejeitada, Denegada e Falha sao finais (Autorizada ainda
/// pode virar Cancelada por evento, na fase 4).
/// </summary>
public enum NfeStatus
{
    /// <summary>Aceita e na fila; ainda sem numero.</summary>
    Recebida = 1,
    /// <summary>O worker esta montando ou transmitindo.</summary>
    EmProcessamento = 2,
    /// <summary>cStat 100: tem protocolo e o XML final (procNFe).</summary>
    Autorizada = 3,
    /// <summary>A SEFAZ recusou (cStat e xMotivo explicam). O numero fica reservado para o reenvio corrigido.</summary>
    Rejeitada = 4,
    /// <summary>cStat 110/301/302/303: irregularidade cadastral. O numero esta consumido.</summary>
    Denegada = 5,
    /// <summary>Enviada sem resposta conclusiva (rede, lote em processamento). O worker vai consultar pela chave antes de reenviar.</summary>
    PendenteConsulta = 6,
    /// <summary>Cancelada por evento autorizado (fase 4).</summary>
    Cancelada = 7,
    /// <summary>O emissor desistiu: erro interno repetido ou pedido que deixou de ser valido. <c>xMotivo</c> explica.</summary>
    Falha = 8
}

public sealed record NfeResponse(
    Guid Id,
    Guid EmitenteId,
    NfeStatus Status,
    AmbienteSefaz Ambiente,
    int Serie,
    long? Numero,
    string? ChaveAcesso,
    string? Protocolo,
    int? CStat,
    string? XMotivo,
    DateTimeOffset? DataEmissao,
    DateTimeOffset? DataAutorizacao,
    int Tentativas,
    DateTimeOffset? ProximaTentativaEm,
    DateTimeOffset CriadaEm,
    DateTimeOffset AtualizadaEm);

/// <summary>Retorno do servico NfeStatusServico4: 107 em operacao, 108/109 paralisado.</summary>
public sealed record StatusServicoSefazResponse(
    int CStat,
    string XMotivo,
    AmbienteSefaz Ambiente,
    string Autorizador,
    DateTimeOffset? DataHora,
    int? TempoMedioSegundos);
