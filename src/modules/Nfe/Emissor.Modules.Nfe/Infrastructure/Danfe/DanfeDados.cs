namespace Emissor.Modules.Nfe.Infrastructure.Danfe;

/// <summary>
/// O que a DANFE imprime, lido do XML da nota (NFe ou nfeProc). E o XML, e
/// nao o pedido, que manda: a DANFE representa o documento assinado e, quando
/// houver, autorizado.
/// </summary>
public sealed record DanfeDados(
    string ChaveAcesso,
    bool Homologacao,
    int TipoOperacao,
    int Serie,
    long Numero,
    string NaturezaOperacao,
    DateTimeOffset DataEmissao,
    DateTimeOffset? DataSaidaEntrada,
    DanfeParte Emitente,
    string? InscricaoEstadualSt,
    int RegimeTributario,
    DanfeParte Destinatario,
    DanfeProtocolo? Protocolo,
    DanfeTotais Totais,
    DanfeTransporte Transporte,
    IReadOnlyList<DanfeItem> Itens,
    string? InformacoesComplementares,
    string? InformacoesFisco);

/// <param name="Documento">CPF ou CNPJ ja formatado com mascara, ou identificacao do estrangeiro.</param>
public sealed record DanfeParte(
    string Nome,
    string? NomeFantasia,
    string Documento,
    string? InscricaoEstadual,
    string Endereco,
    string Bairro,
    string? Cep,
    string Municipio,
    string Uf,
    string? Telefone);

public sealed record DanfeProtocolo(string Numero, DateTimeOffset? Recebimento, int CStat, string XMotivo);

public sealed record DanfeTotais(
    decimal BaseIcms,
    decimal Icms,
    decimal IcmsDesonerado,
    decimal Fcp,
    decimal BaseSt,
    decimal St,
    decimal Produtos,
    decimal Frete,
    decimal Seguro,
    decimal Desconto,
    decimal Outro,
    decimal Ipi,
    decimal Nota,
    decimal? TributosAproximados,
    decimal? BaseIbsCbs,
    decimal? Ibs,
    decimal? Cbs);

public sealed record DanfeTransporte(
    int? ModalidadeFrete,
    string? Nome,
    string? Documento,
    string? InscricaoEstadual,
    string? Endereco,
    string? Municipio,
    string? Uf,
    IReadOnlyList<DanfeVolume> Volumes);

public sealed record DanfeVolume(int? Quantidade, string? Especie, string? Marca, string? Numeracao, decimal? PesoBruto, decimal? PesoLiquido);

/// <param name="SituacaoTributaria">Origem + CST (3 digitos) ou origem + CSOSN (4 digitos), como a DANFE imprime na coluna CST.</param>
public sealed record DanfeItem(
    string Codigo,
    string Descricao,
    string? InformacoesAdicionais,
    string Ncm,
    string SituacaoTributaria,
    int Cfop,
    string Unidade,
    decimal Quantidade,
    decimal ValorUnitario,
    decimal ValorTotal,
    decimal BaseIcms,
    decimal Icms,
    decimal AliquotaIcms,
    decimal Ipi,
    decimal AliquotaIpi);
