using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using Emissor.BuildingBlocks;
using Emissor.Contracts.Emitentes;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Emitentes.Domain;
using Emissor.Modules.Nfe.Application.Ports;
using NFe.Classes.Informacoes;
using NFe.Classes.Informacoes.Destinatario;
using NFe.Classes.Informacoes.Emitente;
using NFe.Classes.Informacoes.Identificacao;
using NFe.Classes.Informacoes.Identificacao.Tipos;
using NFe.Classes.Informacoes.Observacoes;
using NFe.Classes.Informacoes.Pagamento;
using NFe.Classes.Informacoes.Total;
using NFe.Classes.Informacoes.Total.IbsCbs;
using NFe.Classes.Informacoes.Total.IbsCbs.Cbs;
using NFe.Classes.Informacoes.Total.IbsCbs.Ibs;
using NFe.Classes.Informacoes.Transporte;
using NFe.Utils.NFe;
using NFeDocumento = NFe.Classes.NFe;
using ModalidadeFreteDfe = NFe.Classes.Informacoes.Transporte.ModalidadeFrete;
using FormaPagamentoDfe = NFe.Classes.Informacoes.Pagamento.FormaPagamento;
using PresencaCompradorDfe = NFe.Classes.Informacoes.Identificacao.Tipos.PresencaComprador;
using PresencaComprador = Emissor.Contracts.Nfe.PresencaComprador;
using FormaPagamento = Emissor.Contracts.Nfe.FormaPagamento;

namespace Emissor.Modules.Nfe.Infrastructure.Xml;

/// <summary>
/// Monta o XML da NF-e com as classes da DFe.NET. Este arquivo cuida da
/// identificacao, emitente, destinatario, transporte, pagamento e totais; os
/// itens e impostos estao no arquivo <c>DfeNetNfeXmlBuilder.Itens.cs</c>.
/// </summary>
public sealed partial class DfeNetNfeXmlBuilder(Microsoft.Extensions.Options.IOptions<ResponsavelTecnicoOptions> responsavelTecnico) : INfeXmlBuilder
{
    /// <summary>Construtor sem configuracao: sem responsavel tecnico no XML (testes e previa simples).</summary>
    public DfeNetNfeXmlBuilder() : this(Microsoft.Extensions.Options.Options.Create(new ResponsavelTecnicoOptions())) { }

    public const string VersaoLayout = "4.00";
    public const string VersaoAplicativo = "emissor-nf 0.1";
    public const string NomeDestinatarioHomologacao = "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";
    private const int CodigoPaisBrasil = 1058;
    private const string NomePaisBrasil = "BRASIL";
    private const string UfExterior = "EX";

    public Result<string> Montar(NfeMontagem montagem)
    {
        var contexto = new MapeamentoContexto();
        var totais = new Totalizador();
        var pedido = montagem.Pedido;
        var emitente = montagem.Emitente;

        var nfe = new NFeDocumento
        {
            infNFe = new infNFe
            {
                versao = VersaoLayout,
                Id = montagem.Chave.Id,
                ide = MontarIde(montagem, contexto),
                emit = MontarEmit(emitente),
                dest = MontarDest(pedido.Destinatario, emitente, contexto),
                det = MontarItens(pedido.Itens, emitente, contexto, totais),
                transp = MontarTransp(pedido.Transporte, contexto),
                infAdic = MontarInfAdic(pedido.InformacoesAdicionais),
                infRespTec = MontarInfRespTec(responsavelTecnico.Value)
            }
        };

        nfe.infNFe.total = MontarTotal(totais);
        nfe.infNFe.pag = MontarPag(pedido.Pagamento, totais, contexto);

        if (contexto.TemErros)
            return contexto.Falhar<string>();

        return Result<string>.Success(nfe.ObterXmlString());
    }

    private static ide MontarIde(NfeMontagem montagem, MapeamentoContexto contexto)
    {
        var id = montagem.Pedido.Identificacao;
        var chave = montagem.Chave;
        var emitente = montagem.Emitente;
        var ufDestino = montagem.Pedido.Destinatario.Endereco.Uf.Trim().ToUpperInvariant();

        var ide = new ide
        {
            cUF = Enum.Parse<Estado>(emitente.Endereco.Uf),
            cNF = chave.CodigoNumerico,
            natOp = id.NaturezaOperacao.Trim(),
            mod = ModeloDocumento.NFe,
            serie = chave.Serie,
            nNF = chave.Numero,
            dhEmi = montagem.DataEmissao,
            dhSaiEnt = id.DataSaidaEntrada?.ToOffset(montagem.DataEmissao.Offset),
            tpNF = (TipoNFe)id.TipoOperacao,
            idDest = ufDestino == UfExterior
                ? DestinoOperacao.doExterior
                : ufDestino == emitente.Endereco.Uf ? DestinoOperacao.doInterna : DestinoOperacao.doInterestadual,
            cMunFG = long.Parse(emitente.Endereco.CodigoMunicipio),
            tpImp = TipoImpressao.tiRetrato,
            tpEmis = (TipoEmissao)chave.TipoEmissao,
            cDV = chave.DigitoVerificador,
            tpAmb = (TipoAmbiente)emitente.Ambiente,
            finNFe = (FinalidadeNFe)id.Finalidade,
            indFinal = id.ConsumidorFinal ? ConsumidorFinal.cfConsumidorFinal : ConsumidorFinal.cfNao,
            indPres = (PresencaCompradorDfe)id.Presenca,
            procEmi = ProcessoEmissao.peAplicativoContribuinte,
            verProc = VersaoAplicativo
        };

        // Contingencia SVC-AN (tpEmis 6): a chave ja veio com esse tpEmis; o XML leva quando entrou e por que.
        if (emitente.EmContingencia)
        {
            ide.dhCont = Domain.Uf.NoFuso(emitente.ContingenciaDesde!.Value, emitente.Endereco.Uf);
            ide.xJust = emitente.ContingenciaJustificativa;
        }

        // Operacao nao presencial pede o indicador de intermediador (NT 2020.006). Sem marketplace no MVP.
        if (id.Presenca is PresencaComprador.Internet
            or PresencaComprador.Teleatendimento
            or PresencaComprador.EntregaDomicilio
            or PresencaComprador.Outros)
            ide.indIntermed = IndicadorIntermediador.iiSemIntermediador;

        if (id.NotasReferenciadas is { Count: > 0 })
        {
            ide.NFref = [];

            for (var i = 0; i < id.NotasReferenciadas.Count; i++)
            {
                var referenciada = Domain.ChaveAcesso.Parse(id.NotasReferenciadas[i]);

                if (referenciada.IsFailure)
                {
                    contexto.Erro($"identificacao.notasReferenciadas[{i}]", referenciada.Error!.Message);
                    continue;
                }

                ide.NFref.Add(new NFref { refNFe = referenciada.Value!.Valor });
            }
        }

        return ide;
    }

    private static emit MontarEmit(Emitente emitente)
    {
        var endereco = emitente.Endereco;

        var emit = new emit
        {
            xNome = emitente.Nome,
            xFant = emitente.NomeFantasia,
            IE = emitente.InscricaoEstadual,
            CRT = (CRT)emitente.RegimeTributario,
            enderEmit = new enderEmit
            {
                xLgr = endereco.Logradouro,
                nro = endereco.Numero,
                xCpl = endereco.Complemento,
                xBairro = endereco.Bairro,
                cMun = long.Parse(endereco.CodigoMunicipio),
                xMun = endereco.Municipio,
                UF = Enum.Parse<Estado>(endereco.Uf),
                CEP = endereco.Cep,
                cPais = CodigoPaisBrasil,
                xPais = NomePaisBrasil,
                fone = Telefone(endereco.Telefone)
            }
        };

        // O que diferencia o produtor rural PF e so a tag do documento: CPF em vez de CNPJ.
        if (emitente.Tipo == TipoEmitente.PessoaFisica)
            emit.CPF = emitente.Documento;
        else
            emit.CNPJ = emitente.Documento;

        return emit;
    }

    private static dest MontarDest(DestinatarioDto destinatario, Emitente emitente, MapeamentoContexto contexto)
    {
        var dest = new dest(VersaoServico.Versao400)
        {
            // Em homologacao a SEFAZ exige este nome fixo no destinatario (rejeicao 591 sem ele).
            xNome = emitente.Ambiente == AmbienteSefaz.Homologacao ? NomeDestinatarioHomologacao : destinatario.Nome.Trim(),
            indIEDest = (indIEDest)destinatario.IndicadorIe,
            email = string.IsNullOrWhiteSpace(destinatario.Email) ? null : destinatario.Email.Trim()
        };

        var documento = DocumentoFiscal.SomenteCaracteresValidos(destinatario.Documento);

        if (documento.Length == 11 && DocumentoFiscal.IsCpfValido(documento))
            dest.CPF = documento;
        else if (documento.Length == 14 && DocumentoFiscal.IsCnpjValido(documento))
            dest.CNPJ = documento;
        else if (documento.Length == 0 && !string.IsNullOrWhiteSpace(destinatario.IdentificacaoEstrangeiro))
            dest.idEstrangeiro = destinatario.IdentificacaoEstrangeiro.Trim();
        else
            contexto.Erro("destinatario.documento", "Informe um CPF ou CNPJ válido, ou a identificação do estrangeiro.");

        var ie = SomenteDigitos(destinatario.InscricaoEstadual);

        if (destinatario.IndicadorIe == IndicadorIeDestinatario.Contribuinte)
        {
            if (ie.Length is < 2 or > 14)
                contexto.Erro("destinatario.inscricaoEstadual", "Obrigatória para destinatário contribuinte do ICMS.");
            else if (!InscricaoEstadual.IsValida(destinatario.Endereco.Uf, ie))
                contexto.Erro("destinatario.inscricaoEstadual", $"Dígito verificador inválido para uma IE de {destinatario.Endereco.Uf.Trim().ToUpperInvariant()} (a SEFAZ rejeitaria com cStat 210).");

            dest.IE = ie;
        }
        else if (ie.Length > 0)
        {
            contexto.Erro("destinatario.inscricaoEstadual", "Não informe IE para destinatário isento ou não contribuinte.");
        }

        var endereco = destinatario.Endereco;
        var uf = endereco.Uf.Trim().ToUpperInvariant();
        var exterior = uf == UfExterior;

        dest.enderDest = new enderDest
        {
            xLgr = endereco.Logradouro.Trim(),
            nro = endereco.Numero.Trim(),
            xCpl = string.IsNullOrWhiteSpace(endereco.Complemento) ? null : endereco.Complemento.Trim(),
            xBairro = endereco.Bairro.Trim(),
            cMun = exterior ? 9999999 : long.TryParse(endereco.CodigoMunicipio, out var cMun) ? cMun : 0,
            xMun = exterior ? "EXTERIOR" : endereco.Municipio.Trim(),
            UF = uf,
            CEP = exterior ? null : SomenteDigitos(endereco.Cep),
            cPais = exterior ? null : CodigoPaisBrasil,
            xPais = exterior ? null : NomePaisBrasil,
            fone = Telefone(endereco.Telefone)
        };

        if (!exterior && dest.enderDest.cMun == 0)
            contexto.Erro("destinatario.endereco.codigoMunicipio", "Informe o código IBGE do município com 7 dígitos.");

        return dest;
    }

    private static transp MontarTransp(TransporteDto transporte, MapeamentoContexto contexto)
    {
        var transp = new transp { modFrete = (ModalidadeFreteDfe)transporte.ModalidadeFrete };

        if (transporte.Transportador is { } t)
        {
            var transportador = new transporta
            {
                xNome = t.Nome?.Trim(),
                IE = string.IsNullOrWhiteSpace(t.InscricaoEstadual) ? null : SomenteDigitos(t.InscricaoEstadual),
                xEnder = t.EnderecoCompleto?.Trim(),
                xMun = t.Municipio?.Trim(),
                UF = t.Uf?.Trim().ToUpperInvariant()
            };

            var documento = DocumentoFiscal.SomenteCaracteresValidos(t.Documento);

            if (documento.Length == 11)
                transportador.CPF = documento;
            else if (documento.Length == 14)
                transportador.CNPJ = documento;
            else if (documento.Length > 0)
                contexto.Erro("transporte.transportador.documento", "Informe um CPF ou CNPJ.");

            transp.transporta = transportador;
        }

        if (transporte.Volumes is { Count: > 0 })
        {
            transp.vol = transporte.Volumes.Select(v => new vol
            {
                qVol = v.Quantidade,
                esp = v.Especie?.Trim(),
                marca = v.Marca?.Trim(),
                nVol = v.Numeracao?.Trim(),
                pesoL = v.PesoLiquido,
                pesoB = v.PesoBruto
            }).ToList();
        }

        return transp;
    }

    private static List<pag> MontarPag(PagamentoDto pagamento, Totalizador totais, MapeamentoContexto contexto)
    {
        var pag = new pag { detPag = [] };
        var somaPagamentos = 0m;
        var temPagamento = false;

        for (var i = 0; i < pagamento.Detalhes.Count; i++)
        {
            var d = pagamento.Detalhes[i];
            var detalhe = new detPag
            {
                tPag = (FormaPagamentoDfe)d.Forma,
                vPag = d.Valor,
                xPag = string.IsNullOrWhiteSpace(d.Descricao) ? null : d.Descricao.Trim()
            };

            if (d.Indicador is { } indicador)
                detalhe.indPag = (IndicadorPagamentoDetalhePagamento)indicador;

            if (d.Forma == FormaPagamento.SemPagamento)
            {
                if (d.Valor != 0m)
                    contexto.Erro($"pagamento.detalhes[{i}].valor", "Para 'sem pagamento' o valor deve ser zero.");
            }
            else
            {
                temPagamento = true;
                somaPagamentos += d.Valor;
            }

            if (d.Forma == FormaPagamento.Outros && detalhe.xPag is null)
                contexto.Erro($"pagamento.detalhes[{i}].descricao", "Obrigatória quando a forma de pagamento é Outros.");

            pag.detPag.Add(detalhe);
        }

        if (pagamento.Troco is { } troco && troco > 0m)
        {
            pag.vTroco = troco;
            somaPagamentos -= troco;
        }

        // A SEFAZ rejeita quando os pagamentos nao fecham com o total (rejeicoes 865/866). Melhor apontar aqui.
        if (temPagamento && Math.Abs(somaPagamentos - totais.ValorNota) > 0.01m)
            contexto.Erro("pagamento.detalhes", $"A soma dos pagamentos ({somaPagamentos:0.00}) menos o troco difere do total da nota ({totais.ValorNota:0.00}).");

        return [pag];
    }

    private static infAdic? MontarInfAdic(InformacoesAdicionaisDto? info)
    {
        if (info is null || (string.IsNullOrWhiteSpace(info.Fisco) && string.IsNullOrWhiteSpace(info.Contribuinte)))
            return null;

        return new infAdic
        {
            infAdFisco = string.IsNullOrWhiteSpace(info.Fisco) ? null : info.Fisco.Trim(),
            infCpl = string.IsNullOrWhiteSpace(info.Contribuinte) ? null : info.Contribuinte.Trim()
        };
    }

    private static total MontarTotal(Totalizador t)
    {
        var total = new total
        {
            ICMSTot = new ICMSTot
            {
                vBC = t.BaseIcms,
                vICMS = t.Icms,
                vICMSDeson = t.IcmsDesonerado,
                vFCP = t.Fcp,
                vBCST = t.BaseSt,
                vST = t.St,
                vFCPST = t.FcpSt,
                vFCPSTRet = t.FcpStRetido,
                vProd = t.Produtos,
                vFrete = t.Frete,
                vSeg = t.Seguro,
                vDesc = t.Desconto,
                vII = 0m,
                vIPI = t.Ipi,
                vIPIDevol = 0m,
                vPIS = t.Pis,
                vCOFINS = t.Cofins,
                vOutro = t.Outro,
                vNF = t.ValorNota,
                vTotTrib = t.TributosAproximados
            }
        };

        if (t.TemIbsCbs)
        {
            total.IBSCBSTot = new IBSCBSTot
            {
                vBCIBSCBS = t.BaseIbsCbs,
                gIBS = new gIBS
                {
                    gIBSUF = new gIBSUFTotal { vDif = t.IbsUfDiferido, vDevTrib = t.IbsUfDevolvido, vIBSUF = t.IbsUf },
                    gIBSMun = new gIBSMunTotal { vDif = t.IbsMunDiferido, vDevTrib = t.IbsMunDevolvido, vIBSMun = t.IbsMun },
                    vIBS = t.IbsUf + t.IbsMun,
                    vCredPres = 0m,
                    vCredPresCondSus = 0m
                },
                gCBS = new gCBSTotal
                {
                    vDif = t.CbsDiferido,
                    vDevTrib = t.CbsDevolvido,
                    vCBS = t.Cbs,
                    vCredPres = 0m,
                    vCredPresCondSus = 0m
                }
            };
        }

        return total;
    }

    /// <summary>Grupo infRespTec (NT 2018.005): quem desenvolveu o sistema emissor. Opcional no layout, exigido por algumas UFs.</summary>
    private static Shared.NFe.Classes.Informacoes.InfRespTec.infRespTec? MontarInfRespTec(ResponsavelTecnicoOptions opcoes)
    {
        if (!opcoes.Configurado)
            return null;

        return new Shared.NFe.Classes.Informacoes.InfRespTec.infRespTec
        {
            CNPJ = DocumentoFiscal.SomenteCaracteresValidos(opcoes.Cnpj),
            xContato = opcoes.Contato!.Trim(),
            email = opcoes.Email!.Trim(),
            fone = SomenteDigitos(opcoes.Telefone)
        };
    }

    private static long? Telefone(string? telefone)
    {
        var digitos = SomenteDigitos(telefone);

        return digitos.Length is >= 6 and <= 14 ? long.Parse(digitos) : null;
    }

    private static string SomenteDigitos(string? valor) =>
        new((valor ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
}
