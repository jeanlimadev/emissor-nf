namespace Emissor.Modules.Nfe.Infrastructure.Xml;

/// <summary>
/// Responsavel tecnico pelo sistema emissor (grupo <c>infRespTec</c>, NT
/// 2018.005): a empresa que desenvolveu o software, nao o emitente. Vai em
/// todas as notas quando configurado. Secao <c>Nfe:ResponsavelTecnico</c>.
/// </summary>
public sealed class ResponsavelTecnicoOptions
{
    public const string Secao = "Nfe:ResponsavelTecnico";

    public string? Cnpj { get; set; }
    public string? Contato { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }

    public bool Configurado =>
        !string.IsNullOrWhiteSpace(Cnpj) && !string.IsNullOrWhiteSpace(Contato) && !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Telefone);
}
