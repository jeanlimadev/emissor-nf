using Emissor.Modules.Tenancy.Domain;

namespace Emissor.Modules.Tenancy.Tests;

[TestClass]
public sealed class ApiKeyGeneratorTests
{
    [TestMethod]
    public void Chave_gerada_tem_prefixo_reconhecivel_e_hash_confere()
    {
        var gerada = ApiKeyGenerator.Gerar();

        StringAssert.StartsWith(gerada.Chave, ApiKeyGenerator.Prefix);
        Assert.AreEqual(ApiKey.PrefixoLength, gerada.Prefixo.Length);
        Assert.AreEqual(gerada.Prefixo, ApiKeyGenerator.ExtrairPrefixo(gerada.Chave));
        CollectionAssert.AreEqual(gerada.Hash, ApiKeyGenerator.Hash(gerada.Chave));
    }

    [TestMethod]
    public void Duas_chaves_nunca_sao_iguais()
    {
        var a = ApiKeyGenerator.Gerar();
        var b = ApiKeyGenerator.Gerar();

        Assert.AreNotEqual(a.Chave, b.Chave);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("abc")]
    [DataRow("emk_curto_x")]
    [DataRow("outro_abcdefgh_segredo")]
    public void Formato_estranho_nao_tem_prefixo(string? chave) =>
        Assert.IsNull(ApiKeyGenerator.ExtrairPrefixo(chave));

    [TestMethod]
    public void Confere_rejeita_chave_de_outro_hash()
    {
        var agora = DateTimeOffset.UtcNow;
        var criada = ApiKeyFactory.Criar(Guid.NewGuid(), "ERP", agora);
        var (apiKey, chave) = criada.Value;

        Assert.IsTrue(ApiKeyGenerator.Confere(apiKey, chave));
        Assert.IsFalse(ApiKeyGenerator.Confere(apiKey, chave + "x"));
    }

    [TestMethod]
    public void Nome_vazio_e_recusado()
    {
        var criada = ApiKeyFactory.Criar(Guid.NewGuid(), "  ", DateTimeOffset.UtcNow);

        Assert.IsTrue(criada.IsFailure);
        Assert.AreEqual(TenancyErrors.NomeApiKeyInvalido, criada.Error);
    }
}
