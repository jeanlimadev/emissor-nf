using Emissor.BuildingBlocks;

namespace Emissor.BuildingBlocks.Tests;

[TestClass]
public sealed class ResultTests
{
    [TestMethod]
    public void FailAs_carrega_o_mesmo_erro_para_outro_tipo()
    {
        var erro = Error.NotFound("x.nao_encontrado", "Não achei.");
        var original = Result<int>.Failure(erro);

        var convertido = original.FailAs<string>();

        Assert.IsTrue(convertido.IsFailure);
        Assert.AreSame(erro, convertido.Error);
    }

    [TestMethod]
    public void ProblemResults_mapeia_kind_para_status()
    {
        Assert.AreEqual(404, ProblemResults.StatusCodeFor(ErrorKind.NotFound));
        Assert.AreEqual(409, ProblemResults.StatusCodeFor(ErrorKind.Conflict));
        Assert.AreEqual(400, ProblemResults.StatusCodeFor(ErrorKind.Validation));
        Assert.AreEqual(401, ProblemResults.StatusCodeFor(ErrorKind.Unauthorized));
    }
}
