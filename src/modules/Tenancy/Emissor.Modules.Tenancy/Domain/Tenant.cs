using Emissor.BuildingBlocks;

namespace Emissor.Modules.Tenancy.Domain;

/// <summary>
/// Um sistema cliente do emissor: o ERP de um cliente, outro projeto. Dono das
/// API keys, dos emitentes e das notas. Nao e o emitente: um tenant pode ter
/// varios CPFs/CNPJs emitindo.
/// </summary>
public sealed class Tenant
{
    public const int NomeMaxLength = 120;

    private Tenant() { }

    public Guid Id { get; private set; }
    public string Nome { get; private set; } = string.Empty;
    public bool Ativo { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    public static Result<Tenant> Criar(string? nome, DateTimeOffset agora)
    {
        var nomeLimpo = (nome ?? string.Empty).Trim();

        if (nomeLimpo.Length is 0 or > NomeMaxLength)
            return Result<Tenant>.Failure(TenancyErrors.NomeInvalido);

        return Result<Tenant>.Success(new Tenant
        {
            Id = Guid.CreateVersion7(),
            Nome = nomeLimpo,
            Ativo = true,
            CriadoEm = agora
        });
    }

    public void Desativar() => Ativo = false;
    public void Reativar() => Ativo = true;
}
