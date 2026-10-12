using System.Text;
using Emissor.BuildingBlocks;
using Emissor.Contracts.Emitentes;
using Emissor.Modules.Emitentes.Application.Emitentes;
using Emissor.Modules.Emitentes.Application.Ports;
using Emissor.Modules.Emitentes.Domain;

namespace Emissor.Modules.Emitentes.Application.Certificados;

public sealed record UploadCertificadoCommand(Guid EmitenteId, byte[] Pfx, string Senha);

/// <summary>
/// Recebe o pfx e a senha, confere que abre, que tem chave privada, que o
/// titular e o emitente e que nao esta vencido; so entao criptografa e guarda.
/// A senha em texto nao sai deste handler: nem log, nem resposta.
/// </summary>
public sealed class UploadCertificadoHandler(
    IEmitenteRepository emitentes,
    ICertificadoInspector inspector,
    ICertificadoProtector protector,
    ICurrentTenant tenant,
    IClock clock)
{
    /// <summary>Um A1 tem poucos KB; 64 KB ja e folga para cadeias longas.</summary>
    public const int PfxMaxBytes = 64 * 1024;

    public async Task<Result<CertificadoResponse>> HandleAsync(UploadCertificadoCommand command, CancellationToken cancellationToken)
    {
        if (command.Pfx.Length is 0 or > PfxMaxBytes)
            return Result<CertificadoResponse>.Failure(EmitenteErrors.CertificadoMuitoGrande);

        var emitente = await emitentes.GetAsync(command.EmitenteId, cancellationToken);

        if (emitente is null)
            return Result<CertificadoResponse>.Failure(EmitenteErrors.NaoEncontrado);

        var inspecao = inspector.Inspecionar(command.Pfx, command.Senha);

        if (inspecao.IsFailure)
            return inspecao.FailAs<CertificadoResponse>();

        var certificado = Certificado.Criar(
            tenant.TenantId,
            emitente.Id,
            inspecao.Value!,
            protector.Proteger(command.Pfx),
            protector.Proteger(Encoding.UTF8.GetBytes(command.Senha)),
            clock.UtcNow);

        var anterior = emitente.Certificado;
        var definido = emitente.DefinirCertificado(certificado, clock.UtcNow);

        if (definido.IsFailure)
            return Result<CertificadoResponse>.Failure(definido.Error!);

        if (anterior is not null)
            emitentes.RemoveCertificado(anterior);

        emitentes.AddCertificado(certificado);
        await emitentes.SaveChangesAsync(cancellationToken);

        return Result<CertificadoResponse>.Success(EmitenteMapping.ToResponse(certificado));
    }
}

public sealed class GetCertificadoHandler(IEmitenteRepository emitentes)
{
    public async Task<Result<CertificadoResponse>> HandleAsync(Guid emitenteId, CancellationToken cancellationToken)
    {
        var emitente = await emitentes.GetAsync(emitenteId, cancellationToken);

        if (emitente is null)
            return Result<CertificadoResponse>.Failure(EmitenteErrors.NaoEncontrado);

        return emitente.Certificado is null
            ? Result<CertificadoResponse>.Failure(EmitenteErrors.CertificadoNaoEncontrado)
            : Result<CertificadoResponse>.Success(EmitenteMapping.ToResponse(emitente.Certificado));
    }
}

public sealed class RemoveCertificadoHandler(IEmitenteRepository emitentes, IClock clock)
{
    public async Task<Result> HandleAsync(Guid emitenteId, CancellationToken cancellationToken)
    {
        var emitente = await emitentes.GetAsync(emitenteId, cancellationToken);

        if (emitente is null)
            return Result.Failure(EmitenteErrors.NaoEncontrado);

        if (emitente.Certificado is null)
            return Result.Failure(EmitenteErrors.CertificadoNaoEncontrado);

        emitentes.RemoveCertificado(emitente.Certificado);
        emitente.RemoverCertificado(clock.UtcNow);
        await emitentes.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
