using Emissor.BuildingBlocks;

namespace Emissor.Modules.Emitentes.Domain;

/// <summary>
/// Uma serie de numeracao do emitente (campo <c>serie</c> da NF-e) e o
/// proximo numero a usar. A reserva de numero na emissao faz lock nesta
/// linha, dentro da mesma transacao que grava a nota, para dois pedidos ao
/// mesmo tempo nunca pegarem o mesmo numero.
/// </summary>
public sealed class Serie : ITenantOwned
{
    public const int ModeloNfe = 55;
    public const int NumeroMin = 1;
    public const int NumeroMax = 999;
    public const long NumeroNotaMax = 999_999_999;

    private Serie() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid EmitenteId { get; private set; }
    public int Modelo { get; private set; }
    public int Numero { get; private set; }
    public long ProximoNumero { get; private set; }
    public bool Ativa { get; private set; }
    public DateTimeOffset CriadaEm { get; private set; }

    public static Result<Serie> Criar(Guid tenantId, Guid emitenteId, int numero, long proximoNumero, DateTimeOffset agora)
    {
        if (numero is < NumeroMin or > NumeroMax)
            return Result<Serie>.Failure(EmitenteErrors.SerieInvalida);

        if (proximoNumero is < 1 or > NumeroNotaMax)
            return Result<Serie>.Failure(EmitenteErrors.ProximoNumeroInvalido);

        return Result<Serie>.Success(new Serie
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            EmitenteId = emitenteId,
            Modelo = ModeloNfe,
            Numero = numero,
            ProximoNumero = proximoNumero,
            Ativa = true,
            CriadaEm = agora
        });
    }

    /// <summary>Ajuste manual (ex.: migracao de outro sistema). Nunca para tras: numero ja usado nao volta.</summary>
    public Result AjustarProximoNumero(long proximoNumero)
    {
        if (proximoNumero is < 1 or > NumeroNotaMax)
            return Result.Failure(EmitenteErrors.ProximoNumeroInvalido);

        if (proximoNumero < ProximoNumero)
            return Result.Failure(EmitenteErrors.ProximoNumeroRetrocede);

        ProximoNumero = proximoNumero;
        return Result.Success();
    }

    /// <summary>Consome o proximo numero. Chamado dentro da transacao que reservou a linha.</summary>
    public Result<long> ReservarNumero()
    {
        if (!Ativa)
            return Result<long>.Failure(EmitenteErrors.SerieInativa);

        if (ProximoNumero > NumeroNotaMax)
            return Result<long>.Failure(EmitenteErrors.SerieEsgotada);

        var numero = ProximoNumero;
        ProximoNumero++;

        return Result<long>.Success(numero);
    }

    public void Desativar() => Ativa = false;
    public void Reativar() => Ativa = true;
}
