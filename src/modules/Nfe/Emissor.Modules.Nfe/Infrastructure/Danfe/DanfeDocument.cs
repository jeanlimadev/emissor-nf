using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Emissor.Modules.Nfe.Infrastructure.Danfe;

/// <summary>
/// A DANFE em retrato (A4), seguindo o leiaute do Anexo II do Manual de
/// Orientacao do Contribuinte: canhoto, cabecalho com codigo de barras da
/// chave, natureza e protocolo, destinatario, calculo do imposto,
/// transportador e volumes, tabela de produtos (pagina quantas vezes
/// precisar) e dados adicionais. Em homologacao, ou sem protocolo, recebe a
/// marca d'agua "SEM VALOR FISCAL".
/// </summary>
public sealed class DanfeDocument(DanfeDados dados, string? marca) : IDocument
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Color Cinza = Colors.Grey.Lighten2;
    private const float Fonte = 6.5f;
    private const float FonteValor = 8f;
    private const float FonteRotulo = 5f;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"DANFE NF-e {dados.Numero} série {dados.Serie}",
        Author = dados.Emitente.Nome,
        Subject = "Documento Auxiliar da Nota Fiscal Eletrônica",
        CreationDate = DateTimeOffset.Now
    };

    public void Compose(IDocumentContainer documento)
    {
        documento.Page(pagina =>
        {
            pagina.Size(PageSizes.A4);
            pagina.Margin(6, Unit.Millimetre);
            pagina.DefaultTextStyle(estilo => estilo.FontSize(Fonte).FontFamily("Lato"));

            pagina.Foreground().Element(MarcaDagua);
            pagina.Header().Element(Cabecalho);
            pagina.Content().Element(Corpo);
        });
    }

    // ----- marca d'agua -------------------------------------------------------------------------

    private void MarcaDagua(IContainer container)
    {
        var texto = marca ?? (dados.Protocolo is null ? "NF-e SEM AUTORIZAÇÃO DE USO" : dados.Homologacao ? "SEM VALOR FISCAL" : null);

        if (texto is null)
            return;

        // Unconstrained: o texto girado nao pode disputar espaco com o conteudo da pagina.
        // O ponto de giro e o canto superior esquerdo do texto; o deslocamento recoloca o centro dele no centro da pagina.
        container.AlignCenter().AlignMiddle().Unconstrained().OffsetX(-215).OffsetY(-80).Rotate(-30)
            .Text(texto).FontSize(44).Bold().FontColor(Colors.Grey.Lighten1);
    }

    // ----- cabecalho (repete em toda pagina) ----------------------------------------------------

    private void Cabecalho(IContainer container)
    {
        container.Column(coluna =>
        {
            coluna.Item().ShowOnce().Element(Canhoto);
            coluna.Item().Element(QuadroPrincipal);
            coluna.Item().Row(linha =>
            {
                linha.RelativeItem(3).Element(c => Campo(c, "NATUREZA DA OPERAÇÃO", dados.NaturezaOperacao));
                linha.RelativeItem(2).Element(c => Campo(c, "PROTOCOLO DE AUTORIZAÇÃO DE USO",
                    dados.Protocolo is null ? "—" : $"{dados.Protocolo.Numero}  {Data(dados.Protocolo.Recebimento, "dd/MM/yyyy HH:mm:ss")}"));
            });
            coluna.Item().Row(linha =>
            {
                linha.RelativeItem().Element(c => Campo(c, "INSCRIÇÃO ESTADUAL", dados.Emitente.InscricaoEstadual));
                linha.RelativeItem().Element(c => Campo(c, "INSCRIÇÃO ESTADUAL DO SUBST. TRIBUTÁRIO", dados.InscricaoEstadualSt));
                linha.RelativeItem().Element(c => Campo(c, "CNPJ / CPF", dados.Emitente.Documento));
            });
            coluna.Item().PaddingTop(2);
        });
    }

    private void Canhoto(IContainer container)
    {
        container.PaddingBottom(3).Row(linha =>
        {
            linha.RelativeItem(5).Column(c =>
            {
                c.Item().Element(x => Campo(x, string.Empty,
                    $"RECEBEMOS DE {dados.Emitente.Nome.ToUpperInvariant()} OS PRODUTOS/SERVIÇOS CONSTANTES DA NOTA FISCAL ELETRÔNICA INDICADA AO LADO. EMISSÃO: {Data(dados.DataEmissao, "dd/MM/yyyy")}  DEST.: {dados.Destinatario.Nome.ToUpperInvariant()}  VALOR TOTAL: {Moeda(dados.Totais.Nota)}", FonteRotulo + 0.5f));
                c.Item().Row(r =>
                {
                    r.RelativeItem(1).Element(x => Campo(x, "DATA DE RECEBIMENTO", " "));
                    r.RelativeItem(3).Element(x => Campo(x, "IDENTIFICAÇÃO E ASSINATURA DO RECEBEDOR", " "));
                });
            });
            linha.RelativeItem(1).Border(0.5f).Padding(2).AlignCenter().AlignMiddle().Column(c =>
            {
                c.Item().AlignCenter().Text("NF-e").FontSize(9).Bold();
                c.Item().AlignCenter().Text($"Nº {dados.Numero:000,000,000}").FontSize(7).Bold();
                c.Item().AlignCenter().Text($"SÉRIE {dados.Serie:000}").FontSize(7).Bold();
            });
        });
    }

    private void QuadroPrincipal(IContainer container)
    {
        container.Row(linha =>
        {
            // Identificacao do emitente
            linha.RelativeItem(4).Border(0.5f).Padding(3).Column(c =>
            {
                c.Item().Text(dados.Emitente.Nome).FontSize(10).Bold();

                if (dados.Emitente.NomeFantasia is { } fantasia && fantasia != dados.Emitente.Nome)
                    c.Item().Text(fantasia).FontSize(7);

                c.Item().PaddingTop(2).Text($"{dados.Emitente.Endereco}");
                c.Item().Text($"{dados.Emitente.Bairro} - {dados.Emitente.Municipio}/{dados.Emitente.Uf}  CEP {dados.Emitente.Cep}");

                if (dados.Emitente.Telefone is { } fone)
                    c.Item().Text($"Fone: {fone}");
            });

            // DANFE
            linha.RelativeItem(2).Border(0.5f).Padding(3).Column(c =>
            {
                c.Item().AlignCenter().Text("DANFE").FontSize(12).Bold();
                c.Item().AlignCenter().Text("Documento Auxiliar da Nota Fiscal Eletrônica").FontSize(5.5f);
                c.Item().PaddingTop(2).Row(r =>
                {
                    r.RelativeItem().Column(t =>
                    {
                        t.Item().Text("0 - ENTRADA").FontSize(5.5f);
                        t.Item().Text("1 - SAÍDA").FontSize(5.5f);
                    });
                    r.ConstantItem(14).Border(0.5f).AlignCenter().AlignMiddle().Text(dados.TipoOperacao.ToString(CultureInfo.InvariantCulture)).FontSize(9).Bold();
                });
                c.Item().PaddingTop(2).AlignCenter().Text($"Nº {dados.Numero:000,000,000}").FontSize(8).Bold();
                c.Item().AlignCenter().Text($"SÉRIE {dados.Serie:000}").FontSize(8).Bold();
                c.Item().AlignCenter().Text(texto =>
                {
                    texto.DefaultTextStyle(s => s.FontSize(7).Bold());
                    texto.Span("FOLHA ");
                    texto.CurrentPageNumber();
                    texto.Span("/");
                    texto.TotalPages();
                });
            });

            // Codigo de barras e chave
            linha.RelativeItem(4).Column(c =>
            {
                c.Item().Border(0.5f).Padding(3).Height(42).Svg(Code128.Svg(dados.ChaveAcesso)).FitArea();
                c.Item().Element(x => Campo(x, "CHAVE DE ACESSO", DanfeLeitor.FormatarChave(dados.ChaveAcesso), 7.5f, negrito: true));
                c.Item().Border(0.5f).Padding(2).AlignCenter().AlignMiddle().Text(
                    "Consulta de autenticidade no portal nacional da NF-e www.nfe.fazenda.gov.br/portal ou no site da Sefaz Autorizadora").FontSize(5.5f);
            });
        });
    }

    // ----- corpo --------------------------------------------------------------------------------

    private void Corpo(IContainer container)
    {
        container.Column(coluna =>
        {
            coluna.Item().ShowOnce().Element(Destinatario);
            coluna.Item().ShowOnce().Element(CalculoDoImposto);
            coluna.Item().ShowOnce().Element(Transportador);
            coluna.Item().Element(Produtos);
            // ShowEntire: titulo e quadro nunca se separam entre paginas.
            coluna.Item().ShowOnce().ShowEntire().Element(DadosAdicionais);
        });
    }

    private void Destinatario(IContainer container)
    {
        var d = dados.Destinatario;

        container.Column(c =>
        {
            c.Item().Element(x => Titulo(x, "DESTINATÁRIO / REMETENTE"));
            c.Item().Row(r =>
            {
                r.RelativeItem(5).Element(x => Campo(x, "NOME / RAZÃO SOCIAL", d.Nome));
                r.RelativeItem(2).Element(x => Campo(x, "CNPJ / CPF", d.Documento));
                r.RelativeItem(1.5f).Element(x => Campo(x, "DATA DA EMISSÃO", Data(dados.DataEmissao, "dd/MM/yyyy")));
            });
            c.Item().Row(r =>
            {
                r.RelativeItem(5).Element(x => Campo(x, "ENDEREÇO", d.Endereco));
                r.RelativeItem(2).Element(x => Campo(x, "BAIRRO / DISTRITO", d.Bairro));
                r.RelativeItem(1).Element(x => Campo(x, "CEP", d.Cep));
                r.RelativeItem(1.5f).Element(x => Campo(x, "DATA DA SAÍDA/ENTRADA", Data(dados.DataSaidaEntrada, "dd/MM/yyyy")));
            });
            c.Item().Row(r =>
            {
                r.RelativeItem(4).Element(x => Campo(x, "MUNICÍPIO", d.Municipio));
                r.RelativeItem(2).Element(x => Campo(x, "FONE / FAX", d.Telefone));
                r.RelativeItem(0.6f).Element(x => Campo(x, "UF", d.Uf));
                r.RelativeItem(2).Element(x => Campo(x, "INSCRIÇÃO ESTADUAL", d.InscricaoEstadual));
                r.RelativeItem(1.5f).Element(x => Campo(x, "HORA DA SAÍDA/ENTRADA", Data(dados.DataSaidaEntrada, "HH:mm:ss")));
            });
        });
    }

    private void CalculoDoImposto(IContainer container)
    {
        var t = dados.Totais;

        container.Column(c =>
        {
            c.Item().Element(x => Titulo(x, "CÁLCULO DO IMPOSTO"));
            c.Item().Row(r =>
            {
                r.RelativeItem().Element(x => Valor(x, "BASE DE CÁLCULO DO ICMS", t.BaseIcms));
                r.RelativeItem().Element(x => Valor(x, "VALOR DO ICMS", t.Icms));
                r.RelativeItem().Element(x => Valor(x, "BASE DE CÁLCULO DO ICMS ST", t.BaseSt));
                r.RelativeItem().Element(x => Valor(x, "VALOR DO ICMS ST", t.St));
                r.RelativeItem().Element(x => Valor(x, "VALOR DO ICMS DESONERADO", t.IcmsDesonerado));
                r.RelativeItem().Element(x => Valor(x, "V. APROX. TRIBUTOS", t.TributosAproximados));
                r.RelativeItem().Element(x => Valor(x, "VALOR TOTAL DOS PRODUTOS", t.Produtos));
            });
            c.Item().Row(r =>
            {
                r.RelativeItem().Element(x => Valor(x, "VALOR DO FRETE", t.Frete));
                r.RelativeItem().Element(x => Valor(x, "VALOR DO SEGURO", t.Seguro));
                r.RelativeItem().Element(x => Valor(x, "DESCONTO", t.Desconto));
                r.RelativeItem().Element(x => Valor(x, "OUTRAS DESPESAS", t.Outro));
                r.RelativeItem().Element(x => Valor(x, "VALOR DO IPI", t.Ipi));
                r.RelativeItem().Element(x => Valor(x, "VALOR DO FCP", t.Fcp));
                r.RelativeItem().Element(x => Valor(x, "VALOR TOTAL DA NOTA", t.Nota, negrito: true));
            });

            // Reforma Tributaria (NT 2025.002): totais de IBS e CBS quando a nota os traz.
            if (t.BaseIbsCbs is not null)
            {
                c.Item().Row(r =>
                {
                    r.RelativeItem().Element(x => Valor(x, "BASE DE CÁLCULO IBS/CBS", t.BaseIbsCbs));
                    r.RelativeItem().Element(x => Valor(x, "VALOR DO IBS", t.Ibs));
                    r.RelativeItem().Element(x => Valor(x, "VALOR DA CBS", t.Cbs));
                    r.RelativeItem(4).Element(x => Campo(x, " ", " "));
                });
            }
        });
    }

    private void Transportador(IContainer container)
    {
        var tr = dados.Transporte;

        container.Column(c =>
        {
            c.Item().Element(x => Titulo(x, "TRANSPORTADOR / VOLUMES TRANSPORTADOS"));
            c.Item().Row(r =>
            {
                r.RelativeItem(4).Element(x => Campo(x, "RAZÃO SOCIAL", tr.Nome));
                r.RelativeItem(2.5f).Element(x => Campo(x, "FRETE POR CONTA", ModalidadeFrete(tr.ModalidadeFrete)));
                r.RelativeItem(1.5f).Element(x => Campo(x, "CÓDIGO ANTT", null));
                r.RelativeItem(1.5f).Element(x => Campo(x, "PLACA DO VEÍCULO", null));
                r.RelativeItem(0.6f).Element(x => Campo(x, "UF", null));
                r.RelativeItem(2).Element(x => Campo(x, "CNPJ / CPF", tr.Documento));
            });
            c.Item().Row(r =>
            {
                r.RelativeItem(4).Element(x => Campo(x, "ENDEREÇO", tr.Endereco));
                r.RelativeItem(3).Element(x => Campo(x, "MUNICÍPIO", tr.Municipio));
                r.RelativeItem(0.6f).Element(x => Campo(x, "UF", tr.Uf));
                r.RelativeItem(2).Element(x => Campo(x, "INSCRIÇÃO ESTADUAL", tr.InscricaoEstadual));
            });

            foreach (var v in tr.Volumes.DefaultIfEmpty(new DanfeVolume(null, null, null, null, null, null)))
            {
                c.Item().Row(r =>
                {
                    r.RelativeItem(1.5f).Element(x => Campo(x, "QUANTIDADE", v.Quantidade?.ToString(PtBr)));
                    r.RelativeItem(2).Element(x => Campo(x, "ESPÉCIE", v.Especie));
                    r.RelativeItem(2).Element(x => Campo(x, "MARCA", v.Marca));
                    r.RelativeItem(2).Element(x => Campo(x, "NUMERAÇÃO", v.Numeracao));
                    r.RelativeItem(1.5f).Element(x => Campo(x, "PESO BRUTO", v.PesoBruto?.ToString("N3", PtBr), alinharDireita: true));
                    r.RelativeItem(1.5f).Element(x => Campo(x, "PESO LÍQUIDO", v.PesoLiquido?.ToString("N3", PtBr), alinharDireita: true));
                });
            }
        });
    }

    private void Produtos(IContainer container)
    {
        container.Column(c =>
        {
            c.Item().Element(x => Titulo(x, "DADOS DOS PRODUTOS / SERVIÇOS"));
            c.Item().Border(0.5f).Table(tabela =>
            {
                tabela.ColumnsDefinition(colunas =>
                {
                    colunas.RelativeColumn(1.4f);   // codigo
                    colunas.RelativeColumn(4.2f);   // descricao
                    colunas.RelativeColumn(1.0f);   // ncm
                    colunas.RelativeColumn(0.6f);   // cst
                    colunas.RelativeColumn(0.6f);   // cfop
                    colunas.RelativeColumn(0.6f);   // un
                    colunas.RelativeColumn(1.0f);   // quant
                    colunas.RelativeColumn(1.1f);   // v unit
                    colunas.RelativeColumn(1.1f);   // v total
                    colunas.RelativeColumn(1.0f);   // bc icms
                    colunas.RelativeColumn(0.9f);   // v icms
                    colunas.RelativeColumn(0.8f);   // v ipi
                    colunas.RelativeColumn(0.7f);   // aliq icms
                    colunas.RelativeColumn(0.7f);   // aliq ipi
                });

                tabela.Header(h =>
                {
                    foreach (var titulo in new[] { "CÓDIGO", "DESCRIÇÃO DO PRODUTO / SERVIÇO", "NCM/SH", "CST", "CFOP", "UN", "QUANT.", "V. UNITÁRIO", "V. TOTAL", "BC ICMS", "V. ICMS", "V. IPI", "ALÍQ. ICMS", "ALÍQ. IPI" })
                        h.Cell().Background(Cinza).BorderBottom(0.5f).BorderRight(0.25f).Padding(1.5f).AlignCenter().Text(titulo).FontSize(FonteRotulo).Bold();
                });

                foreach (var item in dados.Itens)
                {
                    var descricao = item.InformacoesAdicionais is null ? item.Descricao : $"{item.Descricao}\n{item.InformacoesAdicionais}";

                    Celula(tabela, item.Codigo);
                    Celula(tabela, descricao);
                    Celula(tabela, item.Ncm, centro: true);
                    Celula(tabela, item.SituacaoTributaria, centro: true);
                    Celula(tabela, item.Cfop.ToString(CultureInfo.InvariantCulture), centro: true);
                    Celula(tabela, item.Unidade, centro: true);
                    Celula(tabela, Quantidade(item.Quantidade), direita: true);
                    Celula(tabela, Quantidade(item.ValorUnitario), direita: true);
                    Celula(tabela, Moeda(item.ValorTotal), direita: true);
                    Celula(tabela, Moeda(item.BaseIcms), direita: true);
                    Celula(tabela, Moeda(item.Icms), direita: true);
                    Celula(tabela, Moeda(item.Ipi), direita: true);
                    Celula(tabela, Percentual(item.AliquotaIcms), direita: true);
                    Celula(tabela, Percentual(item.AliquotaIpi), direita: true);
                }
            });
        });
    }

    private void DadosAdicionais(IContainer container)
    {
        container.Column(c =>
        {
            c.Item().Element(x => Titulo(x, "DADOS ADICIONAIS"));
            c.Item().Row(r =>
            {
                var complementares = string.Join("\n", new[] { dados.InformacoesComplementares, dados.InformacoesFisco }.Where(s => !string.IsNullOrWhiteSpace(s)));
                r.RelativeItem(2).MinHeight(60).Element(x => Campo(x, "INFORMAÇÕES COMPLEMENTARES", complementares.Length == 0 ? " " : complementares));
                r.RelativeItem(1).MinHeight(60).Element(x => Campo(x, "RESERVADO AO FISCO", " "));
            });
        });
    }

    // ----- pecas ---------------------------------------------------------------------------------

    private static void Titulo(IContainer container, string texto) =>
        container.PaddingTop(3).PaddingBottom(1).Text(texto).FontSize(6).Bold();

    private static void Campo(IContainer container, string rotulo, string? valor, float tamanho = FonteValor, bool negrito = false, bool alinharDireita = false)
    {
        container.Border(0.5f).Padding(2).Column(c =>
        {
            if (rotulo.Length > 0)
                c.Item().Text(rotulo).FontSize(FonteRotulo);

            var texto = (alinharDireita ? c.Item().AlignRight() : c.Item()).Text(string.IsNullOrWhiteSpace(valor) ? " " : valor).FontSize(tamanho);

            if (negrito)
                texto.Bold();
        });
    }

    private static void Valor(IContainer container, string rotulo, decimal? valor, bool negrito = false) =>
        Campo(container, rotulo, valor is null ? " " : Moeda(valor.Value), FonteValor, negrito, alinharDireita: true);

    private static void Celula(TableDescriptor tabela, string texto, bool centro = false, bool direita = false)
    {
        var celula = tabela.Cell().BorderBottom(0.25f).BorderRight(0.25f).Padding(1.5f);
        var alinhada = centro ? celula.AlignCenter() : direita ? celula.AlignRight() : celula;
        alinhada.Text(texto).FontSize(6);
    }

    private static string Moeda(decimal valor) => valor.ToString("N2", PtBr);

    private static string Quantidade(decimal valor)
    {
        // Ate 4 casas, sem zeros inuteis a direita (mas sempre ao menos 2).
        var texto = valor.ToString("0.00##", PtBr);
        return texto;
    }

    private static string Percentual(decimal valor) => valor == 0m ? string.Empty : valor.ToString("0.00", PtBr);

    private static string Data(DateTimeOffset? data, string formato) => data?.ToString(formato, PtBr) ?? string.Empty;

    private static string ModalidadeFrete(int? modFrete) => modFrete switch
    {
        0 => "0 - Emitente",
        1 => "1 - Destinatário",
        2 => "2 - Terceiros",
        3 => "3 - Próprio (remetente)",
        4 => "4 - Próprio (destinatário)",
        9 => "9 - Sem frete",
        _ => string.Empty
    };
}
