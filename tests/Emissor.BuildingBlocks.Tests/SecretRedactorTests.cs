using Emissor.BuildingBlocks;

namespace Emissor.BuildingBlocks.Tests;

[TestClass]
public sealed class SecretRedactorTests
{
    [TestMethod]
    public void Senha_de_connection_string_e_mascarada()
    {
        var redacted = SecretRedactor.Redact("Host=db;Username=app;Password=segredo123;Database=x");

        Assert.AreEqual("Host=db;Username=app;Password=***;Database=x", redacted);
    }

    [TestMethod]
    public void Api_key_em_json_e_mascarada()
    {
        var redacted = SecretRedactor.Redact("{\"x-api-key\":\"emk_abc\"}");

        Assert.IsFalse(redacted.Contains("emk_abc"), redacted);
    }

    [TestMethod]
    public void Texto_corrido_atravessa_intacto()
    {
        const string texto = "a senha expirou e o token é inválido";

        Assert.AreEqual(texto, SecretRedactor.Redact(texto));
    }
}
