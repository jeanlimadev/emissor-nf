using System.Security.Cryptography;
using System.Text.Json;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Nfe.Domain;

namespace Emissor.Modules.Nfe.Application.Emissao;

internal static class NfeMapping
{
    /// <summary>O pedido vai para o banco como JSON e volta igual quando o worker monta o XML.</summary>
    private static readonly JsonSerializerOptions PedidoJson = new(JsonSerializerDefaults.Web);

    public static NfeResponse ToResponse(NotaFiscal nfe) => new(
        nfe.Id,
        nfe.EmitenteId,
        nfe.Status,
        nfe.Ambiente,
        nfe.Serie,
        nfe.Numero,
        nfe.ChaveAcesso,
        nfe.Protocolo,
        nfe.CStat,
        nfe.XMotivo,
        nfe.DataEmissao,
        nfe.DataAutorizacao,
        nfe.Tentativas,
        nfe.ProximaTentativaEm,
        nfe.CriadaEm,
        nfe.AtualizadaEm);

    public static string SerializarPedido(EmissaoNfeRequest pedido) => JsonSerializer.Serialize(pedido, PedidoJson);

    public static EmissaoNfeRequest DesserializarPedido(string json) =>
        JsonSerializer.Deserialize<EmissaoNfeRequest>(json, PedidoJson)
        ?? throw new InvalidOperationException("Pedido guardado na nota não pôde ser lido.");

    /// <summary>SHA-256 do JSON do pedido: mesma Idempotency-Key com hash diferente e conflito.</summary>
    public static string HashDoPedido(string pedidoJson) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(pedidoJson)));
}
