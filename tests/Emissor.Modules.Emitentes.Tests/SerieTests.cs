using Emissor.Modules.Emitentes.Domain;

namespace Emissor.Modules.Emitentes.Tests;

[TestClass]
public sealed class SerieTests
{
    private static readonly DateTimeOffset Agora = DateTimeOffset.UtcNow;

    [TestMethod]
    public void Reservar_numero_entrega_o_proximo_e_avanca()
    {
        var serie = Serie.Criar(Guid.NewGuid(), Guid.NewGuid(), 1, 10, Agora).Value!;

        var primeiro = serie.ReservarNumero();
        var segundo = serie.ReservarNumero();

        Assert.AreEqual(10L, primeiro.Value);
        Assert.AreEqual(11L, segundo.Value);
        Assert.AreEqual(12L, serie.ProximoNumero);
    }

    [TestMethod]
    public void Serie_fora_da_faixa_e_recusada()
    {
        Assert.AreEqual(EmitenteErrors.SerieInvalida, Serie.Criar(Guid.NewGuid(), Guid.NewGuid(), 0, 1, Agora).Error);
        Assert.AreEqual(EmitenteErrors.SerieInvalida, Serie.Criar(Guid.NewGuid(), Guid.NewGuid(), 1000, 1, Agora).Error);
    }

    [TestMethod]
    public void Ajuste_nao_retrocede()
    {
        var serie = Serie.Criar(Guid.NewGuid(), Guid.NewGuid(), 1, 100, Agora).Value!;

        Assert.AreEqual(EmitenteErrors.ProximoNumeroRetrocede, serie.AjustarProximoNumero(99).Error);
        Assert.IsTrue(serie.AjustarProximoNumero(100).IsSuccess);
        Assert.IsTrue(serie.AjustarProximoNumero(500).IsSuccess);
    }

    [TestMethod]
    public void Serie_inativa_nao_reserva()
    {
        var serie = Serie.Criar(Guid.NewGuid(), Guid.NewGuid(), 1, 1, Agora).Value!;
        serie.Desativar();

        Assert.AreEqual(EmitenteErrors.SerieInativa, serie.ReservarNumero().Error);
    }

    [TestMethod]
    public void Serie_esgotada_nao_reserva()
    {
        var serie = Serie.Criar(Guid.NewGuid(), Guid.NewGuid(), 1, Serie.NumeroNotaMax, Agora).Value!;

        Assert.IsTrue(serie.ReservarNumero().IsSuccess);
        Assert.AreEqual(EmitenteErrors.SerieEsgotada, serie.ReservarNumero().Error);
    }
}
