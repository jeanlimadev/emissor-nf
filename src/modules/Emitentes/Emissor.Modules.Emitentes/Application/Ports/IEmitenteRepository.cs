using Emissor.BuildingBlocks;
using Emissor.Modules.Emitentes.Domain;

namespace Emissor.Modules.Emitentes.Application.Ports;

/// <summary>Tudo aqui ja vem filtrado pelo tenant corrente: o filtro global do DbContext garante.</summary>
public interface IEmitenteRepository
{
    Task<Emitente?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ExisteDocumentoAsync(string documento, Guid? exceto, CancellationToken cancellationToken);
    Task<PagedResult<Emitente>> ListAsync(PageRequest? page, bool? ativo, CancellationToken cancellationToken);
    Task AddAsync(Emitente emitente, CancellationToken cancellationToken);
    /// <summary>
    /// Marca o certificado como novo. Explicito porque o EF, ao descobrir a
    /// entidade pela navegacao com o Guid ja preenchido, a trataria como
    /// existente e emitiria UPDATE em vez de INSERT.
    /// </summary>
    void AddCertificado(Certificado certificado);

    void RemoveCertificado(Certificado certificado);
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Certificados que vencem ate a data, de TODOS os tenants (ignora o filtro
    /// global). So para o aviso de vencimento, que roda sem tenant amarrado.
    /// </summary>
    Task<IReadOnlyList<CertificadoVencendo>> ListarCertificadosVencendoAsync(DateTimeOffset ate, CancellationToken cancellationToken);
}

public sealed record CertificadoVencendo(Guid TenantId, Guid EmitenteId, string Nome, string Documento, DateTimeOffset ValidoAte);

public interface ISerieRepository
{
    Task<Serie?> GetAsync(Guid emitenteId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Serie>> ListByEmitenteAsync(Guid emitenteId, CancellationToken cancellationToken);
    Task<bool> ExisteAsync(Guid emitenteId, int modelo, int numero, CancellationToken cancellationToken);
    Task AddAsync(Serie serie, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Consome o proximo numero da serie de forma atomica (um UPDATE com
    /// RETURNING): dois pedidos ao mesmo tempo nunca recebem o mesmo numero.
    /// O numero sai reservado mesmo que a nota depois falhe; furo na numeracao
    /// se resolve com inutilizacao, nunca reaproveitando.
    /// </summary>
    Task<Result<long>> ReservarNumeroAsync(Guid emitenteId, int numeroSerie, CancellationToken cancellationToken);
}
