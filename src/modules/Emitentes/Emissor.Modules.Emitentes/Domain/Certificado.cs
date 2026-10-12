using Emissor.BuildingBlocks;

namespace Emissor.Modules.Emitentes.Domain;

/// <summary>
/// O certificado A1 do emitente, guardado criptografado. O que fica em claro
/// sao os metadados que a operacao precisa ver: titular, documento, validade
/// e thumbprint. O pfx e a senha so sao abertos na hora de assinar.
/// </summary>
public sealed class Certificado : ITenantOwned
{
    private Certificado() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid EmitenteId { get; private set; }
    public string Titular { get; private set; } = string.Empty;
    public string Documento { get; private set; } = string.Empty;
    public string Thumbprint { get; private set; } = string.Empty;
    public DateTimeOffset ValidoDe { get; private set; }
    public DateTimeOffset ValidoAte { get; private set; }
    public byte[] PfxProtegido { get; private set; } = [];
    public byte[] SenhaProtegida { get; private set; } = [];
    public DateTimeOffset EnviadoEm { get; private set; }

    public static Certificado Criar(
        Guid tenantId,
        Guid emitenteId,
        CertificadoInspecionado inspecao,
        byte[] pfxProtegido,
        byte[] senhaProtegida,
        DateTimeOffset agora) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            EmitenteId = emitenteId,
            Titular = inspecao.Titular,
            Documento = inspecao.Documento,
            Thumbprint = inspecao.Thumbprint,
            ValidoDe = inspecao.ValidoDe,
            ValidoAte = inspecao.ValidoAte,
            PfxProtegido = pfxProtegido,
            SenhaProtegida = senhaProtegida,
            EnviadoEm = agora
        };

    public bool VenceEm(TimeSpan prazo, DateTimeOffset agora) => ValidoAte <= agora + prazo;
}

/// <summary>O que se le de um pfx aberto: quem e o titular e ate quando vale.</summary>
public sealed record CertificadoInspecionado(
    string Titular,
    string Documento,
    string Thumbprint,
    DateTimeOffset ValidoDe,
    DateTimeOffset ValidoAte);
