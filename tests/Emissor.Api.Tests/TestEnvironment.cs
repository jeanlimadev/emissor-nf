using System.Runtime.CompilerServices;

namespace Emissor.Api.Tests;

internal static class TestEnvironment
{
    /// <summary>
    /// O host de teste recarrega a configuracao por inotify, e a maquina de
    /// desenvolvimento pode estar no limite de instancias (128 por usuario).
    /// Polling evita o erro sem mexer no sistema; e o mesmo que se faz em CI.
    /// </summary>
    [ModuleInitializer]
    internal static void UsePollingFileWatcher()
    {
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "1");
        Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");

        // A assinatura da NF-e e RSA-SHA1; em Fedora/RHEL o OpenSSL precisa ser liberado antes do primeiro uso.
        Emissor.BuildingBlocks.OpenSslSha1.PermitirSeNecessario();
    }
}
