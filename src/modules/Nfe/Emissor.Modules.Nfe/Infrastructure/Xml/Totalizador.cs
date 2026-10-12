namespace Emissor.Modules.Nfe.Infrastructure.Xml;

/// <summary>
/// Soma os valores dos itens para os grupos <c>ICMSTot</c> e <c>IBSCBSTot</c>.
/// Nao e motor fiscal: cada valor ja veio calculado no item, aqui so se soma e
/// se fecha o total da nota (<c>vNF</c>) com a formula do layout.
/// </summary>
internal sealed class Totalizador
{
    public decimal BaseIcms;
    public decimal Icms;
    public decimal IcmsDesonerado;
    public decimal IcmsDesoneradoDeduzido;
    public decimal Fcp;
    public decimal BaseSt;
    public decimal St;
    public decimal FcpSt;
    public decimal FcpStRetido;
    public decimal Produtos;
    public decimal Frete;
    public decimal Seguro;
    public decimal Desconto;
    public decimal Ipi;
    public decimal Pis;
    public decimal Cofins;
    public decimal Outro;
    public decimal? TributosAproximados;

    public bool TemIbsCbs;
    public decimal BaseIbsCbs;
    public decimal IbsUf;
    public decimal IbsUfDiferido;
    public decimal IbsUfDevolvido;
    public decimal IbsMun;
    public decimal IbsMunDiferido;
    public decimal IbsMunDevolvido;
    public decimal Cbs;
    public decimal CbsDiferido;
    public decimal CbsDevolvido;

    /// <summary>
    /// vNF = vProd - vDesc - vICMSDeson(deduzido) + vST + vFCPST + vFrete + vSeg + vOutro + vII + vIPI + vIPIDevol.
    /// Sem II nem IPI devolvido no MVP.
    /// </summary>
    public decimal ValorNota =>
        Produtos - Desconto - IcmsDesoneradoDeduzido + St + FcpSt + Frete + Seguro + Outro + Ipi;

    public void SomarTributos(decimal? valor)
    {
        if (valor is null)
            return;

        TributosAproximados = (TributosAproximados ?? 0m) + valor.Value;
    }
}
