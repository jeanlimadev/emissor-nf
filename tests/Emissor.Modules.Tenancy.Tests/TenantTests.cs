using Emissor.Modules.Tenancy.Domain;

namespace Emissor.Modules.Tenancy.Tests;

[TestClass]
public sealed class TenantTests
{
    [TestMethod]
    public void Tenant_nasce_ativo_com_nome_limpo()
    {
        var tenant = Tenant.Criar("  ERP FLV  ", DateTimeOffset.UtcNow);

        Assert.IsTrue(tenant.IsSuccess);
        Assert.AreEqual("ERP FLV", tenant.Value!.Nome);
        Assert.IsTrue(tenant.Value.Ativo);
    }

    [TestMethod]
    public void Nome_longo_demais_e_recusado()
    {
        var tenant = Tenant.Criar(new string('a', Tenant.NomeMaxLength + 1), DateTimeOffset.UtcNow);

        Assert.IsTrue(tenant.IsFailure);
        Assert.AreEqual(TenancyErrors.NomeInvalido, tenant.Error);
    }
}
