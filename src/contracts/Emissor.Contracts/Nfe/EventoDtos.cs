namespace Emissor.Contracts.Nfe;

/// <summary>Codigo do evento (campo <c>tpEvento</c>).</summary>
public enum TipoEventoNfe
{
    CartaCorrecao = 110110,
    Cancelamento = 110111
}

public enum EventoStatus
{
    /// <summary>Montado e assinado, mas a SEFAZ nao respondeu de forma conclusiva. Repita a chamada.</summary>
    Pendente = 1,
    /// <summary>cStat 135 (vinculado) ou 136 (registrado, nao vinculado).</summary>
    Registrado = 2,
    Rejeitado = 3
}

/// <param name="Justificativa">15 a 255 caracteres. Vai para a SEFAZ como <c>xJust</c>.</param>
public sealed record CancelamentoRequest(string Justificativa);

/// <param name="Correcao">15 a 1000 caracteres (<c>xCorrecao</c>). Nao pode corrigir valores, quantidades, datas nem as partes.</param>
public sealed record CartaCorrecaoRequest(string Correcao);

public sealed record EventoNfeResponse(
    Guid Id,
    Guid NfeId,
    TipoEventoNfe Tipo,
    int Sequencia,
    EventoStatus Status,
    string Texto,
    string? Protocolo,
    int? CStat,
    string? XMotivo,
    DateTimeOffset DataEvento,
    DateTimeOffset? RegistradoEm,
    DateTimeOffset CriadoEm);

/// <param name="Serie">Serie da numeracao a inutilizar.</param>
/// <param name="NumeroInicial">Primeiro numero da faixa (inclusive).</param>
/// <param name="NumeroFinal">Ultimo numero da faixa (inclusive). Igual ao inicial para um numero so.</param>
/// <param name="Justificativa">15 a 255 caracteres.</param>
public sealed record InutilizacaoRequest(int Serie, long NumeroInicial, long NumeroFinal, string Justificativa);

public sealed record InutilizacaoResponse(
    Guid Id,
    Guid EmitenteId,
    int Serie,
    long NumeroInicial,
    long NumeroFinal,
    int Ano,
    EventoStatus Status,
    string Justificativa,
    string? Protocolo,
    int? CStat,
    string? XMotivo,
    DateTimeOffset? HomologadaEm,
    DateTimeOffset CriadaEm);
