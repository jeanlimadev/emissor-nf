using Emissor.Contracts.Emitentes;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Emitentes.Domain;
using NFe.Classes.Informacoes.Detalhe;
using NFe.Classes.Informacoes.Detalhe.Tributacao;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado.InformacoesIbsCbs;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado.InformacoesIbsCbs.InformacoesCbs;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado.InformacoesIbsCbs.InformacoesIbs;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado.Tipos;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Estadual;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Estadual.Tipos;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Federal;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Federal.Tipos;
using OrigemMercadoriaDfe = NFe.Classes.Informacoes.Detalhe.Tributacao.Estadual.Tipos.OrigemMercadoria;

namespace Emissor.Modules.Nfe.Infrastructure.Xml;

/// <summary>Itens (<c>det</c>): produto e impostos. Cada CST/CSOSN vira a classe correspondente da DFe.NET.</summary>
public sealed partial class DfeNetNfeXmlBuilder
{
    private const string SemGtin = "SEM GTIN";
    private const string CodigoEnquadramentoIpiPadrao = "999";

    private static List<det> MontarItens(IReadOnlyList<ItemNfeDto> itens, Emitente emitente, MapeamentoContexto contexto, Totalizador totais)
    {
        var lista = new List<det>(itens.Count);

        for (var i = 0; i < itens.Count; i++)
        {
            var item = itens[i];
            var produto = MontarProd(item.Produto, i, contexto, totais);

            lista.Add(new det
            {
                nItem = i + 1,
                prod = produto,
                imposto = MontarImposto(item.Impostos, emitente, i, contexto, totais),
                infAdProd = string.IsNullOrWhiteSpace(item.InformacoesAdicionais) ? null : item.InformacoesAdicionais.Trim()
            });
        }

        return lista;
    }

    private static prod MontarProd(ProdutoDto p, int indice, MapeamentoContexto contexto, Totalizador totais)
    {
        var valorCalculado = decimal.Round(p.Quantidade * p.ValorUnitario, 2, MidpointRounding.AwayFromZero);
        var valorTotal = p.ValorTotal ?? valorCalculado;

        // A SEFAZ rejeita (629) quando vProd foge de qCom x vUnCom alem da tolerancia.
        if (Math.Abs(valorTotal - valorCalculado) > 0.01m)
            contexto.Erro(MapeamentoContexto.Item(indice, "produto.valorTotal"), $"Difere de quantidade x valor unitário ({valorCalculado:0.00}).");

        var codigoBarras = string.IsNullOrWhiteSpace(p.CodigoBarras) ? SemGtin : p.CodigoBarras.Trim();
        var compoeTotal = p.CompoeTotal ?? true;

        var prod = new prod
        {
            cProd = p.Codigo.Trim(),
            cEAN = codigoBarras,
            xProd = p.Descricao.Trim(),
            NCM = SomenteDigitos(p.Ncm),
            CEST = string.IsNullOrWhiteSpace(p.Cest) ? null : SomenteDigitos(p.Cest),
            cBenef = string.IsNullOrWhiteSpace(p.CodigoBeneficioFiscal) ? null : p.CodigoBeneficioFiscal.Trim(),
            CFOP = p.Cfop,
            uCom = p.Unidade.Trim(),
            qCom = p.Quantidade,
            vUnCom = p.ValorUnitario,
            vProd = valorTotal,
            cEANTrib = string.IsNullOrWhiteSpace(p.CodigoBarrasTributavel) ? codigoBarras : p.CodigoBarrasTributavel.Trim(),
            uTrib = string.IsNullOrWhiteSpace(p.UnidadeTributavel) ? p.Unidade.Trim() : p.UnidadeTributavel.Trim(),
            qTrib = p.QuantidadeTributavel ?? p.Quantidade,
            vUnTrib = p.ValorUnitarioTributavel ?? p.ValorUnitario,
            vFrete = ValorOpcional(p.Frete),
            vSeg = ValorOpcional(p.Seguro),
            vDesc = ValorOpcional(p.Desconto),
            vOutro = ValorOpcional(p.OutrasDespesas),
            indTot = compoeTotal ? IndicadorTotal.ValorDoItemCompoeTotalNF : IndicadorTotal.ValorDoItemNaoCompoeTotalNF,
            xPed = string.IsNullOrWhiteSpace(p.NumeroPedido) ? null : p.NumeroPedido.Trim(),
            nItemPed = p.ItemPedido
        };

        if (compoeTotal)
        {
            totais.Produtos += valorTotal;
            totais.Frete += p.Frete ?? 0m;
            totais.Seguro += p.Seguro ?? 0m;
            totais.Desconto += p.Desconto ?? 0m;
            totais.Outro += p.OutrasDespesas ?? 0m;
        }

        return prod;
    }

    /// <summary>Zero nao vai no XML: o layout trata o campo como opcional e a SEFAZ prefere a ausencia.</summary>
    private static decimal? ValorOpcional(decimal? valor) => valor is null or 0m ? null : valor;

    private static imposto MontarImposto(ImpostosItemDto impostos, Emitente emitente, int indice, MapeamentoContexto contexto, Totalizador totais)
    {
        totais.SomarTributos(impostos.ValorAproximadoTributos);

        var imposto = new imposto
        {
            vTotTrib = impostos.ValorAproximadoTributos,
            ICMS = new ICMS { TipoICMS = MontarIcms(impostos.Icms, emitente, indice, contexto, totais) },
            PIS = new PIS { TipoPIS = MontarPis(impostos.Pis, indice, contexto, totais) },
            COFINS = new COFINS { TipoCOFINS = MontarCofins(impostos.Cofins, indice, contexto, totais) }
        };

        if (impostos.Ipi is not null)
            imposto.IPI = MontarIpi(impostos.Ipi, indice, contexto, totais);

        if (impostos.IbsCbs is not null)
            imposto.IBSCBS = MontarIbsCbs(impostos.IbsCbs, indice, contexto, totais);

        return imposto;
    }

    // ----- ICMS ---------------------------------------------------------------------------------

    private static ICMSBasico MontarIcms(IcmsDto icms, Emitente emitente, int indice, MapeamentoContexto contexto, Totalizador totais)
    {
        var campo = (string resto) => MapeamentoContexto.Item(indice, "impostos.icms." + resto);
        var simples = emitente.RegimeTributario is RegimeTributario.SimplesNacional or RegimeTributario.MicroempreendedorIndividual;

        if (simples)
        {
            if (string.IsNullOrWhiteSpace(icms.Csosn))
            {
                contexto.Erro(campo("csosn"), "Emitente do Simples Nacional informa CSOSN, não CST.");
                return new ICMSSN102();
            }

            return MontarIcmsSimples(icms, campo, contexto, totais);
        }

        if (string.IsNullOrWhiteSpace(icms.Cst))
        {
            contexto.Erro(campo("cst"), "Emitente do regime normal informa CST, não CSOSN.");
            return new ICMS40();
        }

        return MontarIcmsRegimeNormal(icms, campo, contexto, totais);
    }

    private static ICMSBasico MontarIcmsRegimeNormal(IcmsDto icms, Func<string, string> campo, MapeamentoContexto contexto, Totalizador totais)
    {
        var orig = (OrigemMercadoriaDfe)icms.Origem;
        var cst = contexto.Codigo<Csticms>(icms.Cst, "Cst", campo("cst"), "CST do ICMS");

        switch (icms.Cst!.Trim())
        {
            case "00":
            {
                var r = new ICMS00
                {
                    orig = orig,
                    CST = cst,
                    modBC = (DeterminacaoBaseIcms)contexto.Exigir(icms.ModalidadeBaseCalculo, campo("modalidadeBaseCalculo"), "para CST 00"),
                    vBC = contexto.Exigir(icms.BaseCalculo, campo("baseCalculo"), "para CST 00"),
                    pICMS = contexto.Exigir(icms.Aliquota, campo("aliquota"), "para CST 00"),
                    vICMS = contexto.Exigir(icms.Valor, campo("valor"), "para CST 00")
                };
                Fcp(icms, (p, v) => { r.pFCP = p; r.vFCP = v; }, totais);
                totais.BaseIcms += r.vBC;
                totais.Icms += r.vICMS;
                return r;
            }
            case "10":
            {
                var r = new ICMS10
                {
                    orig = orig,
                    CST = cst,
                    modBC = (DeterminacaoBaseIcms)contexto.Exigir(icms.ModalidadeBaseCalculo, campo("modalidadeBaseCalculo"), "para CST 10"),
                    vBC = contexto.Exigir(icms.BaseCalculo, campo("baseCalculo"), "para CST 10"),
                    pICMS = contexto.Exigir(icms.Aliquota, campo("aliquota"), "para CST 10"),
                    vICMS = contexto.Exigir(icms.Valor, campo("valor"), "para CST 10"),
                    modBCST = (DeterminacaoBaseIcmsSt)contexto.Exigir(icms.ModalidadeBaseCalculoSt, campo("modalidadeBaseCalculoSt"), "para CST 10"),
                    pMVAST = icms.PercentualMvaSt,
                    pRedBCST = icms.PercentualReducaoBaseSt,
                    vBCST = contexto.Exigir(icms.BaseCalculoSt, campo("baseCalculoSt"), "para CST 10"),
                    pICMSST = contexto.Exigir(icms.AliquotaSt, campo("aliquotaSt"), "para CST 10"),
                    vICMSST = contexto.Exigir(icms.ValorSt, campo("valorSt"), "para CST 10")
                };
                Fcp(icms, (p, v) => { r.vBCFCP = icms.BaseCalculoFcp; r.pFCP = p; r.vFCP = v; }, totais);
                totais.BaseIcms += r.vBC;
                totais.Icms += r.vICMS;
                totais.BaseSt += r.vBCST;
                totais.St += r.vICMSST;
                FcpSt(icms, (b, p, v) => { r.vBCFCPST = b; r.pFCPST = p; r.vFCPST = v; }, totais);
                return r;
            }
            case "20":
            {
                var r = new ICMS20
                {
                    orig = orig,
                    CST = cst,
                    modBC = (DeterminacaoBaseIcms)contexto.Exigir(icms.ModalidadeBaseCalculo, campo("modalidadeBaseCalculo"), "para CST 20"),
                    pRedBC = contexto.Exigir(icms.PercentualReducaoBase, campo("percentualReducaoBase"), "para CST 20"),
                    vBC = contexto.Exigir(icms.BaseCalculo, campo("baseCalculo"), "para CST 20"),
                    pICMS = contexto.Exigir(icms.Aliquota, campo("aliquota"), "para CST 20"),
                    vICMS = contexto.Exigir(icms.Valor, campo("valor"), "para CST 20")
                };
                Fcp(icms, (p, v) => { r.vBCFCP = icms.BaseCalculoFcp; r.pFCP = p; r.vFCP = v; }, totais);
                Desoneracao(icms, campo, contexto, totais, (v, m, d) => { r.vICMSDeson = v; r.motDesICMS = m; r.indDeduzDeson = d; });
                totais.BaseIcms += r.vBC;
                totais.Icms += r.vICMS;
                return r;
            }
            case "30":
            {
                var r = new ICMS30
                {
                    orig = orig,
                    CST = cst,
                    modBCST = (DeterminacaoBaseIcmsSt)contexto.Exigir(icms.ModalidadeBaseCalculoSt, campo("modalidadeBaseCalculoSt"), "para CST 30"),
                    pMVAST = icms.PercentualMvaSt,
                    pRedBCST = icms.PercentualReducaoBaseSt,
                    vBCST = contexto.Exigir(icms.BaseCalculoSt, campo("baseCalculoSt"), "para CST 30"),
                    pICMSST = contexto.Exigir(icms.AliquotaSt, campo("aliquotaSt"), "para CST 30"),
                    vICMSST = contexto.Exigir(icms.ValorSt, campo("valorSt"), "para CST 30")
                };
                Desoneracao(icms, campo, contexto, totais, (v, m, d) => { r.vICMSDeson = v; r.motDesICMS = m; r.indDeduzDeson = d; });
                totais.BaseSt += r.vBCST;
                totais.St += r.vICMSST;
                FcpSt(icms, (b, p, v) => { r.vBCFCPST = b; r.pFCPST = p; r.vFCPST = v; }, totais);
                return r;
            }
            case "40":
            case "41":
            case "50":
            {
                var r = new ICMS40 { orig = orig, CST = cst };
                Desoneracao(icms, campo, contexto, totais, (v, m, d) => { r.vICMSDeson = v; r.motDesICMS = m; r.indDeduzDeson = d; });
                return r;
            }
            case "51":
            {
                // Diferimento: comum para produtor rural. Tudo opcional no layout; o que vier, vai.
                var r = new ICMS51
                {
                    orig = orig,
                    CST = cst,
                    modBC = icms.ModalidadeBaseCalculo is { } m ? (DeterminacaoBaseIcms)m : null,
                    pRedBC = icms.PercentualReducaoBase,
                    vBC = icms.BaseCalculo,
                    pICMS = icms.Aliquota,
                    vICMSOp = icms.ValorOperacao,
                    pDif = icms.PercentualDiferimento,
                    vICMSDif = icms.ValorDiferido,
                    vICMS = icms.Valor
                };
                Fcp(icms, (p, v) => { r.vBCFCP = icms.BaseCalculoFcp; r.pFCP = p; r.vFCP = v; }, totais);
                totais.BaseIcms += r.vBC ?? 0m;
                totais.Icms += r.vICMS ?? 0m;
                return r;
            }
            case "60":
            {
                var r = new ICMS60
                {
                    orig = orig,
                    CST = cst,
                    vBCSTRet = icms.BaseCalculoStRetido,
                    pST = icms.AliquotaSuportadaConsumidorFinal,
                    vICMSSubstituto = icms.ValorIcmsSubstituto,
                    vICMSSTRet = icms.ValorStRetido
                };
                FcpStRetido(icms, (b, p, v) => { r.vBCFCPSTRet = b; r.pFCPSTRet = p; r.vFCPSTRet = v; }, totais);
                return r;
            }
            case "70":
            {
                var r = new ICMS70
                {
                    orig = orig,
                    CST = cst,
                    modBC = (DeterminacaoBaseIcms)contexto.Exigir(icms.ModalidadeBaseCalculo, campo("modalidadeBaseCalculo"), "para CST 70"),
                    pRedBC = contexto.Exigir(icms.PercentualReducaoBase, campo("percentualReducaoBase"), "para CST 70"),
                    vBC = contexto.Exigir(icms.BaseCalculo, campo("baseCalculo"), "para CST 70"),
                    pICMS = contexto.Exigir(icms.Aliquota, campo("aliquota"), "para CST 70"),
                    vICMS = contexto.Exigir(icms.Valor, campo("valor"), "para CST 70"),
                    modBCST = (DeterminacaoBaseIcmsSt)contexto.Exigir(icms.ModalidadeBaseCalculoSt, campo("modalidadeBaseCalculoSt"), "para CST 70"),
                    pMVAST = icms.PercentualMvaSt,
                    pRedBCST = icms.PercentualReducaoBaseSt,
                    vBCST = contexto.Exigir(icms.BaseCalculoSt, campo("baseCalculoSt"), "para CST 70"),
                    pICMSST = contexto.Exigir(icms.AliquotaSt, campo("aliquotaSt"), "para CST 70"),
                    vICMSST = contexto.Exigir(icms.ValorSt, campo("valorSt"), "para CST 70")
                };
                Fcp(icms, (p, v) => { r.vBCFCP = icms.BaseCalculoFcp; r.pFCP = p; r.vFCP = v; }, totais);
                Desoneracao(icms, campo, contexto, totais, (v, m, d) => { r.vICMSDeson = v; r.motDesICMS = m; r.indDeduzDeson = d; });
                totais.BaseIcms += r.vBC;
                totais.Icms += r.vICMS;
                totais.BaseSt += r.vBCST;
                totais.St += r.vICMSST;
                FcpSt(icms, (b, p, v) => { r.vBCFCPST = b; r.pFCPST = p; r.vFCPST = v; }, totais);
                return r;
            }
            case "90":
            {
                var r = new ICMS90
                {
                    orig = orig,
                    CST = cst,
                    modBC = icms.ModalidadeBaseCalculo is { } m ? (DeterminacaoBaseIcms)m : null,
                    vBC = icms.BaseCalculo,
                    pRedBC = icms.PercentualReducaoBase,
                    pICMS = icms.Aliquota,
                    vICMS = icms.Valor,
                    modBCST = icms.ModalidadeBaseCalculoSt is { } mst ? (DeterminacaoBaseIcmsSt)mst : null,
                    pMVAST = icms.PercentualMvaSt,
                    pRedBCST = icms.PercentualReducaoBaseSt,
                    vBCST = icms.BaseCalculoSt,
                    pICMSST = icms.AliquotaSt,
                    vICMSST = icms.ValorSt
                };
                Fcp(icms, (p, v) => { r.vBCFCP = icms.BaseCalculoFcp; r.pFCP = p; r.vFCP = v; }, totais);
                Desoneracao(icms, campo, contexto, totais, (v, m, d) => { r.vICMSDeson = v; r.motDesICMS = m; r.indDeduzDeson = d; });
                totais.BaseIcms += r.vBC ?? 0m;
                totais.Icms += r.vICMS ?? 0m;
                totais.BaseSt += r.vBCST ?? 0m;
                totais.St += r.vICMSST ?? 0m;
                FcpSt(icms, (b, p, v) => { r.vBCFCPST = b; r.pFCPST = p; r.vFCPST = v; }, totais);
                return r;
            }
            default:
                contexto.Erro(campo("cst"), $"CST '{icms.Cst}' não é suportado. Use 00, 10, 20, 30, 40, 41, 50, 51, 60, 70 ou 90.");
                return new ICMS40 { orig = orig, CST = Csticms.Cst41 };
        }
    }

    private static ICMSBasico MontarIcmsSimples(IcmsDto icms, Func<string, string> campo, MapeamentoContexto contexto, Totalizador totais)
    {
        var orig = (OrigemMercadoriaDfe)icms.Origem;
        var csosn = contexto.Codigo<Csosnicms>(icms.Csosn, "Csosn", campo("csosn"), "CSOSN");

        switch (icms.Csosn!.Trim())
        {
            case "101":
                return new ICMSSN101
                {
                    orig = orig,
                    CSOSN = csosn,
                    pCredSN = contexto.Exigir(icms.PercentualCreditoSimples, campo("percentualCreditoSimples"), "para CSOSN 101"),
                    vCredICMSSN = contexto.Exigir(icms.ValorCreditoSimples, campo("valorCreditoSimples"), "para CSOSN 101")
                };
            case "102":
            case "103":
            case "300":
            case "400":
                return new ICMSSN102 { orig = orig, CSOSN = csosn };
            case "201":
            {
                var r = new ICMSSN201
                {
                    orig = orig,
                    CSOSN = csosn,
                    modBCST = (DeterminacaoBaseIcmsSt)contexto.Exigir(icms.ModalidadeBaseCalculoSt, campo("modalidadeBaseCalculoSt"), "para CSOSN 201"),
                    pMVAST = icms.PercentualMvaSt,
                    pRedBCST = icms.PercentualReducaoBaseSt,
                    vBCST = contexto.Exigir(icms.BaseCalculoSt, campo("baseCalculoSt"), "para CSOSN 201"),
                    pICMSST = contexto.Exigir(icms.AliquotaSt, campo("aliquotaSt"), "para CSOSN 201"),
                    vICMSST = contexto.Exigir(icms.ValorSt, campo("valorSt"), "para CSOSN 201"),
                    pCredSN = contexto.Exigir(icms.PercentualCreditoSimples, campo("percentualCreditoSimples"), "para CSOSN 201"),
                    vCredICMSSN = contexto.Exigir(icms.ValorCreditoSimples, campo("valorCreditoSimples"), "para CSOSN 201")
                };
                totais.BaseSt += r.vBCST;
                totais.St += r.vICMSST;
                FcpSt(icms, (b, p, v) => { r.vBCFCPST = b; r.pFCPST = p; r.vFCPST = v; }, totais);
                return r;
            }
            case "202":
            case "203":
            {
                var r = new ICMSSN202
                {
                    orig = orig,
                    CSOSN = csosn,
                    modBCST = (DeterminacaoBaseIcmsSt)contexto.Exigir(icms.ModalidadeBaseCalculoSt, campo("modalidadeBaseCalculoSt"), "para CSOSN 202/203"),
                    pMVAST = icms.PercentualMvaSt,
                    pRedBCST = icms.PercentualReducaoBaseSt,
                    vBCST = contexto.Exigir(icms.BaseCalculoSt, campo("baseCalculoSt"), "para CSOSN 202/203"),
                    pICMSST = contexto.Exigir(icms.AliquotaSt, campo("aliquotaSt"), "para CSOSN 202/203"),
                    vICMSST = contexto.Exigir(icms.ValorSt, campo("valorSt"), "para CSOSN 202/203")
                };
                totais.BaseSt += r.vBCST;
                totais.St += r.vICMSST;
                FcpSt(icms, (b, p, v) => { r.vBCFCPST = b; r.pFCPST = p; r.vFCPST = v; }, totais);
                return r;
            }
            case "500":
            {
                var r = new ICMSSN500
                {
                    orig = orig,
                    CSOSN = csosn,
                    vBCSTRet = icms.BaseCalculoStRetido,
                    pST = icms.AliquotaSuportadaConsumidorFinal,
                    vICMSSubstituto = icms.ValorIcmsSubstituto,
                    vICMSSTRet = icms.ValorStRetido
                };
                FcpStRetido(icms, (b, p, v) => { r.vBCFCPSTRet = b; r.pFCPSTRet = p; r.vFCPSTRet = v; }, totais);
                return r;
            }
            case "900":
            {
                var r = new ICMSSN900
                {
                    orig = orig,
                    CSOSN = csosn,
                    modBC = icms.ModalidadeBaseCalculo is { } m ? (DeterminacaoBaseIcms)m : null,
                    vBC = icms.BaseCalculo,
                    pRedBC = icms.PercentualReducaoBase,
                    pICMS = icms.Aliquota,
                    vICMS = icms.Valor,
                    modBCST = icms.ModalidadeBaseCalculoSt is { } mst ? (DeterminacaoBaseIcmsSt)mst : null,
                    pMVAST = icms.PercentualMvaSt,
                    pRedBCST = icms.PercentualReducaoBaseSt,
                    vBCST = icms.BaseCalculoSt,
                    pICMSST = icms.AliquotaSt,
                    vICMSST = icms.ValorSt,
                    pCredSN = icms.PercentualCreditoSimples,
                    vCredICMSSN = icms.ValorCreditoSimples
                };
                totais.BaseIcms += r.vBC ?? 0m;
                totais.Icms += r.vICMS ?? 0m;
                totais.BaseSt += r.vBCST ?? 0m;
                totais.St += r.vICMSST ?? 0m;
                FcpSt(icms, (b, p, v) => { r.vBCFCPST = b; r.pFCPST = p; r.vFCPST = v; }, totais);
                return r;
            }
            default:
                contexto.Erro(campo("csosn"), $"CSOSN '{icms.Csosn}' não é suportado. Use 101, 102, 103, 201, 202, 203, 300, 400, 500 ou 900.");
                return new ICMSSN102 { orig = orig, CSOSN = Csosnicms.Csosn102 };
        }
    }

    private static void Fcp(IcmsDto icms, Action<decimal, decimal> aplicar, Totalizador totais)
    {
        if (icms.PercentualFcp is null && icms.ValorFcp is null)
            return;

        var percentual = icms.PercentualFcp ?? 0m;
        var valor = icms.ValorFcp ?? 0m;
        aplicar(percentual, valor);
        totais.Fcp += valor;
    }

    private static void FcpSt(IcmsDto icms, Action<decimal?, decimal?, decimal?> aplicar, Totalizador totais)
    {
        if (icms.BaseCalculoFcpSt is null && icms.PercentualFcpSt is null && icms.ValorFcpSt is null)
            return;

        aplicar(icms.BaseCalculoFcpSt ?? 0m, icms.PercentualFcpSt ?? 0m, icms.ValorFcpSt ?? 0m);
        totais.FcpSt += icms.ValorFcpSt ?? 0m;
    }

    private static void FcpStRetido(IcmsDto icms, Action<decimal?, decimal?, decimal?> aplicar, Totalizador totais)
    {
        if (icms.BaseCalculoFcpStRetido is null && icms.PercentualFcpStRetido is null && icms.ValorFcpStRetido is null)
            return;

        aplicar(icms.BaseCalculoFcpStRetido ?? 0m, icms.PercentualFcpStRetido ?? 0m, icms.ValorFcpStRetido ?? 0m);
        totais.FcpStRetido += icms.ValorFcpStRetido ?? 0m;
    }

    private static void Desoneracao(
        IcmsDto icms,
        Func<string, string> campo,
        MapeamentoContexto contexto,
        Totalizador totais,
        Action<decimal?, MotivoDesoneracaoIcms?, DeduzDesoneracaoNoProduto?> aplicar)
    {
        if (icms.ValorDesonerado is null)
            return;

        var motivo = icms.MotivoDesoneracao is { } m && Enum.IsDefined((MotivoDesoneracaoIcms)m)
            ? (MotivoDesoneracaoIcms?)(MotivoDesoneracaoIcms)m
            : null;

        if (motivo is null)
            contexto.Erro(campo("motivoDesoneracao"), "Obrigatório (e precisa ser um código válido) quando há valor desonerado.");

        var deduz = icms.DeduzDesoneracaoDoTotal == true;
        aplicar(icms.ValorDesonerado, motivo, deduz ? DeduzDesoneracaoNoProduto.Deduz : DeduzDesoneracaoNoProduto.NaoDeduz);

        totais.IcmsDesonerado += icms.ValorDesonerado.Value;

        if (deduz)
            totais.IcmsDesoneradoDeduzido += icms.ValorDesonerado.Value;
    }

    // ----- PIS / COFINS -------------------------------------------------------------------------

    private static PISBasico MontarPis(PisDto pis, int indice, MapeamentoContexto contexto, Totalizador totais)
    {
        var campo = (string resto) => MapeamentoContexto.Item(indice, "impostos.pis." + resto);
        var cst = contexto.Codigo<CSTPIS>(pis.Cst, "pis", campo("cst"), "CST do PIS");
        var valor = 0m;
        PISBasico resultado;

        switch (Grupo(pis.Cst))
        {
            case GrupoPisCofins.Aliquota:
                valor = contexto.Exigir(pis.Valor, campo("valor"), "para CST 01/02");
                resultado = new PISAliq
                {
                    CST = cst,
                    vBC = contexto.Exigir(pis.BaseCalculo, campo("baseCalculo"), "para CST 01/02"),
                    pPIS = contexto.Exigir(pis.Aliquota, campo("aliquota"), "para CST 01/02"),
                    vPIS = valor
                };
                break;
            case GrupoPisCofins.Quantidade:
                valor = contexto.Exigir(pis.Valor, campo("valor"), "para CST 03");
                resultado = new PISQtde
                {
                    CST = cst,
                    qBCProd = contexto.Exigir(pis.QuantidadeVendida, campo("quantidadeVendida"), "para CST 03"),
                    vAliqProd = contexto.Exigir(pis.AliquotaEmReais, campo("aliquotaEmReais"), "para CST 03"),
                    vPIS = valor
                };
                break;
            case GrupoPisCofins.NaoTributado:
                resultado = new PISNT { CST = cst };
                break;
            default:
                valor = pis.Valor ?? 0m;
                resultado = new PISOutr
                {
                    CST = cst,
                    vBC = pis.QuantidadeVendida is null ? pis.BaseCalculo ?? 0m : null,
                    pPIS = pis.QuantidadeVendida is null ? pis.Aliquota ?? 0m : null,
                    qBCProd = pis.QuantidadeVendida,
                    vAliqProd = pis.QuantidadeVendida is null ? null : pis.AliquotaEmReais ?? 0m,
                    vPIS = valor
                };
                break;
        }

        totais.Pis += valor;
        return resultado;
    }

    private static COFINSBasico MontarCofins(CofinsDto cofins, int indice, MapeamentoContexto contexto, Totalizador totais)
    {
        var campo = (string resto) => MapeamentoContexto.Item(indice, "impostos.cofins." + resto);
        var cst = contexto.Codigo<CSTCOFINS>(cofins.Cst, "cofins", campo("cst"), "CST da COFINS");
        var valor = 0m;
        COFINSBasico resultado;

        switch (Grupo(cofins.Cst))
        {
            case GrupoPisCofins.Aliquota:
                valor = contexto.Exigir(cofins.Valor, campo("valor"), "para CST 01/02");
                resultado = new COFINSAliq
                {
                    CST = cst,
                    vBC = contexto.Exigir(cofins.BaseCalculo, campo("baseCalculo"), "para CST 01/02"),
                    pCOFINS = contexto.Exigir(cofins.Aliquota, campo("aliquota"), "para CST 01/02"),
                    vCOFINS = valor
                };
                break;
            case GrupoPisCofins.Quantidade:
                valor = contexto.Exigir(cofins.Valor, campo("valor"), "para CST 03");
                resultado = new COFINSQtde
                {
                    CST = cst,
                    qBCProd = contexto.Exigir(cofins.QuantidadeVendida, campo("quantidadeVendida"), "para CST 03"),
                    vAliqProd = contexto.Exigir(cofins.AliquotaEmReais, campo("aliquotaEmReais"), "para CST 03"),
                    vCOFINS = valor
                };
                break;
            case GrupoPisCofins.NaoTributado:
                resultado = new COFINSNT { CST = cst };
                break;
            default:
                valor = cofins.Valor ?? 0m;
                resultado = new COFINSOutr
                {
                    CST = cst,
                    vBC = cofins.QuantidadeVendida is null ? cofins.BaseCalculo ?? 0m : null,
                    pCOFINS = cofins.QuantidadeVendida is null ? cofins.Aliquota ?? 0m : null,
                    qBCProd = cofins.QuantidadeVendida,
                    vAliqProd = cofins.QuantidadeVendida is null ? null : cofins.AliquotaEmReais ?? 0m,
                    vCOFINS = valor
                };
                break;
        }

        totais.Cofins += valor;
        return resultado;
    }

    private enum GrupoPisCofins { Aliquota, Quantidade, NaoTributado, Outras }

    /// <summary>01/02 aliquota; 03 quantidade; 04 a 09 sem incidencia; o resto (49 a 99) e "outras operacoes".</summary>
    private static GrupoPisCofins Grupo(string? cst) => (cst ?? string.Empty).Trim() switch
    {
        "01" or "02" => GrupoPisCofins.Aliquota,
        "03" => GrupoPisCofins.Quantidade,
        "04" or "05" or "06" or "07" or "08" or "09" => GrupoPisCofins.NaoTributado,
        _ => GrupoPisCofins.Outras
    };

    // ----- IPI ----------------------------------------------------------------------------------

    private static IPI MontarIpi(IpiDto ipi, int indice, MapeamentoContexto contexto, Totalizador totais)
    {
        var campo = (string resto) => MapeamentoContexto.Item(indice, "impostos.ipi." + resto);
        var cst = contexto.Codigo<CSTIPI>(ipi.Cst, "ipi", campo("cst"), "CST do IPI");
        var enquadramento = string.IsNullOrWhiteSpace(ipi.CodigoEnquadramento) ? CodigoEnquadramentoIpiPadrao : ipi.CodigoEnquadramento.Trim();

        if (!int.TryParse(enquadramento, out var cEnq))
        {
            contexto.Erro(campo("codigoEnquadramento"), "Deve ser numérico (3 dígitos).");
            cEnq = 999;
        }

        var resultado = new IPI { cEnq = cEnq };

        if ((ipi.Cst ?? string.Empty).Trim() is "00" or "49" or "50" or "99")
        {
            var valor = contexto.Exigir(ipi.Valor, campo("valor"), "para IPI tributado");
            resultado.TipoIPI = new IPITrib
            {
                CST = cst,
                vBC = contexto.Exigir(ipi.BaseCalculo, campo("baseCalculo"), "para IPI tributado"),
                pIPI = contexto.Exigir(ipi.Aliquota, campo("aliquota"), "para IPI tributado"),
                vIPI = valor
            };
            totais.Ipi += valor;
        }
        else
        {
            resultado.TipoIPI = new IPINT { CST = cst };
        }

        return resultado;
    }

    // ----- IBS / CBS (Reforma Tributaria, NT 2025.002) ------------------------------------------

    private static IBSCBS MontarIbsCbs(IbsCbsDto dto, int indice, MapeamentoContexto contexto, Totalizador totais)
    {
        var campo = (string resto) => MapeamentoContexto.Item(indice, "impostos.ibsCbs." + resto);
        var classificacao = contexto.Exigir(dto.ClassificacaoTributaria, campo("classificacaoTributaria"), "no grupo IBS/CBS (cClassTrib, 6 dígitos)");

        if (classificacao.Length > 0 && (classificacao.Length != 6 || !classificacao.All(char.IsAsciiDigit)))
            contexto.Erro(campo("classificacaoTributaria"), "O código de classificação tributária tem 6 dígitos.");

        var resultado = new IBSCBS
        {
            CST = contexto.Codigo<CST>(dto.Cst, "Cst", campo("cst"), "CST do IBS/CBS"),
            cClassTrib = classificacao
        };

        var temValores = dto.BaseCalculo is not null || dto.IbsUf is not null || dto.IbsMunicipal is not null || dto.Cbs is not null;

        if (!temValores)
            return resultado;

        var ibsUf = dto.IbsUf ?? Exigido(contexto, campo("ibsUf"));
        var ibsMun = dto.IbsMunicipal ?? Exigido(contexto, campo("ibsMunicipal"));
        var cbs = dto.Cbs ?? Exigido(contexto, campo("cbs"));
        var baseCalculo = contexto.Exigir(dto.BaseCalculo, campo("baseCalculo"), "quando há valores de IBS/CBS");

        var grupo = new gIBSCBS
        {
            vBC = baseCalculo,
            gIBSUF = new gIBSUF { pIBSUF = ibsUf.Aliquota, vIBSUF = ibsUf.Valor, gDif = Dif(ibsUf), gDevTrib = DevTrib(ibsUf), gRed = Red(ibsUf) },
            gIBSMun = new gIBSMun { pIBSMun = ibsMun.Aliquota, vIBSMun = ibsMun.Valor, gDif = Dif(ibsMun), gDevTrib = DevTrib(ibsMun), gRed = Red(ibsMun) },
            vIBS = ibsUf.Valor + ibsMun.Valor,
            gCBS = new gCBS { pCBS = cbs.Aliquota, vCBS = cbs.Valor, gDif = Dif(cbs), gDevTrib = DevTrib(cbs), gRed = Red(cbs) }
        };

        if (dto.TributacaoRegular is { } regular)
        {
            grupo.gTribRegular = new gTribRegular
            {
                CSTReg = contexto.Codigo<CST>(regular.Cst, "Cst", campo("tributacaoRegular.cst"), "CST da tributação regular"),
                cClassTribReg = regular.ClassificacaoTributaria.Trim(),
                pAliqEfetRegIBSUF = regular.AliquotaEfetivaIbsUf,
                vTribRegIBSUF = regular.ValorIbsUf,
                pAliqEfetRegIBSMun = regular.AliquotaEfetivaIbsMunicipal,
                vTribRegIBSMun = regular.ValorIbsMunicipal,
                pAliqEfetRegCBS = regular.AliquotaEfetivaCbs,
                vTribRegCBS = regular.ValorCbs
            };
        }

        resultado.gIBSCBS = grupo;

        totais.TemIbsCbs = true;
        totais.BaseIbsCbs += baseCalculo;
        totais.IbsUf += ibsUf.Valor;
        totais.IbsUfDiferido += ibsUf.ValorDiferimento ?? 0m;
        totais.IbsUfDevolvido += ibsUf.ValorDevolucaoTributo ?? 0m;
        totais.IbsMun += ibsMun.Valor;
        totais.IbsMunDiferido += ibsMun.ValorDiferimento ?? 0m;
        totais.IbsMunDevolvido += ibsMun.ValorDevolucaoTributo ?? 0m;
        totais.Cbs += cbs.Valor;
        totais.CbsDiferido += cbs.ValorDiferimento ?? 0m;
        totais.CbsDevolvido += cbs.ValorDevolucaoTributo ?? 0m;

        return resultado;
    }

    private static TributoIbsCbsDto Exigido(MapeamentoContexto contexto, string campo)
    {
        contexto.Erro(campo, "Obrigatório quando há valores de IBS/CBS: informe os três grupos (ibsUf, ibsMunicipal, cbs).");
        return new TributoIbsCbsDto(0m, 0m, null, null, null, null, null);
    }

    private static gDif? Dif(TributoIbsCbsDto t) =>
        t.PercentualDiferimento is null && t.ValorDiferimento is null
            ? null
            : new gDif { pDif = t.PercentualDiferimento ?? 0m, vDif = t.ValorDiferimento ?? 0m };

    private static gDevTrib? DevTrib(TributoIbsCbsDto t) =>
        t.ValorDevolucaoTributo is null ? null : new gDevTrib { vDevTrib = t.ValorDevolucaoTributo.Value };

    private static gRed? Red(TributoIbsCbsDto t) =>
        t.PercentualReducaoAliquota is null && t.AliquotaEfetiva is null
            ? null
            : new gRed { pRedAliq = t.PercentualReducaoAliquota ?? 0m, pAliqEfet = t.AliquotaEfetiva ?? t.Aliquota };
}
