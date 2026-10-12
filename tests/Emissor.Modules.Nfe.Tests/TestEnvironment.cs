using System.Runtime.CompilerServices;
using Emissor.BuildingBlocks;

namespace Emissor.Modules.Nfe.Tests;

internal static class TestEnvironment
{
    /// <summary>A assinatura da NF-e e RSA-SHA1; em Fedora/RHEL o OpenSSL precisa ser liberado antes do primeiro uso de criptografia.</summary>
    [ModuleInitializer]
    internal static void PermitirSha1() => OpenSslSha1.PermitirSeNecessario();
}
