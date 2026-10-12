namespace Emissor.BuildingBlocks;

/// <summary>
/// O tenant (sistema cliente) dono da requisicao ou do trabalho em andamento.
/// Vem da API key autenticada, nunca do corpo do pedido. Todo handler que
/// grava ou le dado de negocio filtra por ele, e o <c>DbContext</c> de cada
/// modulo aplica o filtro global com o mesmo valor.
/// </summary>
public interface ICurrentTenant
{
    /// <exception cref="InvalidOperationException">Nenhum tenant amarrado ao escopo.</exception>
    Guid TenantId { get; }

    /// <summary>Nome do tenant, so para logs e respostas; nunca para decidir acesso.</summary>
    string Nome { get; }

    bool IsBound { get; }
}

/// <summary>
/// O tenant de um trabalho que roda fora de uma requisicao HTTP (worker de
/// transmissao, varredura de certificados). Amarrado uma unica vez ao abrir o
/// escopo do trabalho. Nao e thread-safe, e nao precisa ser: a amarracao
/// pertence a quem abre o escopo, antes de qualquer handler existir.
/// </summary>
public sealed class AmbientTenant : ICurrentTenant
{
    private Guid? _tenantId;
    private string? _nome;

    public void Bind(Guid tenantId, string nome)
    {
        if (_tenantId is not null)
            throw new InvalidOperationException("Escopo de trabalho já amarrado a um tenant. Abra um escopo por unidade de trabalho.");

        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant do trabalho não pode ser vazio.", nameof(tenantId));

        _tenantId = tenantId;
        _nome = nome;
    }

    public Guid TenantId => _tenantId ?? throw new InvalidOperationException(
        "Trabalho sem tenant amarrado: abra o escopo pelo caminho que amarra o tenant ambiente.");

    public string Nome => _nome ?? string.Empty;

    public bool IsBound => _tenantId is not null;
}

/// <summary>Entidade que pertence a um tenant. O filtro global do EF Core se apoia nisto.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
