using Emissor.BuildingBlocks;
using Emissor.Modules.Emitentes.Domain;

namespace Emissor.Modules.Emitentes.Application.Ports;

/// <summary>Criptografa e abre o pfx e a senha com a chave mestra do servico.</summary>
public interface ICertificadoProtector
{
    byte[] Proteger(ReadOnlySpan<byte> dados);
    byte[] Abrir(ReadOnlySpan<byte> protegido);
}

/// <summary>Abre o pfx com a senha e le os metadados do titular.</summary>
public interface ICertificadoInspector
{
    Result<CertificadoInspecionado> Inspecionar(byte[] pfx, string senha);
}
