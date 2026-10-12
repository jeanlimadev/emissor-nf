using Emissor.BuildingBlocks;

namespace Emissor.BuildingBlocks.Tests;

[TestClass]
public sealed class InscricaoEstadualTests
{
    [TestMethod]
    [DataRow("RJ", "11.464.67-0", true, DisplayName = "IE real do RJ com mascara")]
    [DataRow("RJ", "12345674", true, DisplayName = "IE do RJ calculada")]
    [DataRow("RJ", "99887766", false, DisplayName = "IE do RJ com DV errado (a que a SEFAZ rejeitou com 210)")]
    [DataRow("RJ", "1234567", false, DisplayName = "RJ exige 8 digitos")]
    [DataRow("SP", "110042490114", true, DisplayName = "outra UF: so o tamanho")]
    [DataRow("SP", "1", false, DisplayName = "curta demais")]
    [DataRow("RJ", "", false, DisplayName = "vazia")]
    public void Confere_o_digito_do_rj_e_o_tamanho_das_demais(string uf, string ie, bool esperado) =>
        Assert.AreEqual(esperado, InscricaoEstadual.IsValida(uf, ie));
}
