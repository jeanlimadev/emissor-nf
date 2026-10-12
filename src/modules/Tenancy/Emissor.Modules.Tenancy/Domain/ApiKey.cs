namespace Emissor.Modules.Tenancy.Domain;

/// <summary>
/// Uma chave de acesso de um tenant. So o hash fica guardado; o texto da chave
/// e mostrado uma unica vez, na criacao. O prefixo (visivel) serve para achar
/// o registro sem percorrer a tabela e para a pessoa reconhecer qual chave e.
/// </summary>
public sealed class ApiKey
{
    public const int NomeMaxLength = 80;
    public const int PrefixoLength = 8;
    public const int HashLength = 32;

    private ApiKey() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Nome { get; private set; } = string.Empty;
    public string Prefixo { get; private set; } = string.Empty;
    public byte[] Hash { get; private set; } = [];
    public DateTimeOffset CriadaEm { get; private set; }
    public DateTimeOffset? RevogadaEm { get; private set; }
    public DateTimeOffset? UltimoUsoEm { get; private set; }

    public bool Ativa => RevogadaEm is null;

    internal static ApiKey Criar(Guid tenantId, string nome, string prefixo, byte[] hash, DateTimeOffset agora) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Nome = nome,
            Prefixo = prefixo,
            Hash = hash,
            CriadaEm = agora
        };

    public void Revogar(DateTimeOffset agora)
    {
        if (RevogadaEm is null)
            RevogadaEm = agora;
    }

    public void RegistrarUso(DateTimeOffset agora) => UltimoUsoEm = agora;
}
