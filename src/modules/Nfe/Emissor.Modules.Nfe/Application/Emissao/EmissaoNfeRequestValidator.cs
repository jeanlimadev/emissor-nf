using Emissor.BuildingBlocks;
using Emissor.Contracts.Emitentes;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Nfe.Domain;
using FluentValidation;
using FluentValidation.Results;

namespace Emissor.Modules.Nfe.Application.Emissao;

/// <summary>
/// Validacao estrutural do pedido (tamanhos, formatos, obrigatorios do
/// envelope). As regras que dependem do CST escolhido ficam no mapeamento
/// para o XML, que conhece cada grupo.
/// </summary>
public sealed class EmissaoNfeRequestValidator : AbstractValidator<EmissaoNfeRequest>
{
    public const int MaxItens = 990;

    public EmissaoNfeRequestValidator()
    {
        RuleFor(x => x.Identificacao).NotNull().SetValidator(new IdentificacaoValidator()!);
        RuleFor(x => x.Destinatario).NotNull().SetValidator(new DestinatarioValidator()!);
        RuleFor(x => x.Transporte).NotNull().WithMessage("Informe o transporte (ao menos a modalidade de frete).");
        RuleFor(x => x.Transporte!.ModalidadeFrete).IsInEnum().When(x => x.Transporte is not null);
        RuleFor(x => x.Pagamento).NotNull().SetValidator(new PagamentoValidator()!);

        RuleFor(x => x.Itens)
            .NotNull().WithMessage("Informe ao menos um item.")
            .Must(i => i.Count > 0).WithMessage("Informe ao menos um item.")
            .Must(i => i.Count <= MaxItens).WithMessage($"A NF-e aceita até {MaxItens} itens.");
        RuleForEach(x => x.Itens).SetValidator(new ItemValidator());

        When(x => x.InformacoesAdicionais is not null, () =>
        {
            RuleFor(x => x.InformacoesAdicionais!.Fisco).MaximumLength(2000);
            RuleFor(x => x.InformacoesAdicionais!.Contribuinte).MaximumLength(5000);
        });
    }

    /// <summary>Converte o resultado do FluentValidation no <see cref="Error"/> do pedido, com os campos em camelCase como no JSON.</summary>
    public static Error ParaErro(ValidationResult resultado) =>
        NfeErrors.PedidoInvalido.ComDetalhes(resultado.Errors
            .GroupBy(e => CaminhoJson(e.PropertyName), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray(), StringComparer.Ordinal));

    private static string CaminhoJson(string propriedade) =>
        string.Join('.', propriedade.Split('.').Select(seg => seg.Length == 0 ? seg : char.ToLowerInvariant(seg[0]) + seg[1..]));

    private sealed class IdentificacaoValidator : AbstractValidator<IdentificacaoNfeDto>
    {
        public IdentificacaoValidator()
        {
            RuleFor(x => x.Serie).InclusiveBetween(0, 999);
            RuleFor(x => x.NaturezaOperacao).NotEmpty().MaximumLength(60);
            RuleFor(x => x.TipoOperacao).IsInEnum();
            RuleFor(x => x.Finalidade).IsInEnum();
            RuleFor(x => x.Presenca).IsInEnum();
            RuleForEach(x => x.NotasReferenciadas)
                .Must(c => ChaveAcesso.Parse(c).IsSuccess)
                .WithMessage("Chave de acesso inválida (44 dígitos com DV correto).");
        }
    }

    private sealed class DestinatarioValidator : AbstractValidator<DestinatarioDto>
    {
        public DestinatarioValidator()
        {
            RuleFor(x => x.Nome).NotEmpty().MaximumLength(60);
            RuleFor(x => x.IndicadorIe).IsInEnum();
            RuleFor(x => x.Email).MaximumLength(60);
            RuleFor(x => x.IdentificacaoEstrangeiro).MaximumLength(20);
            RuleFor(x => x.Endereco).NotNull().WithMessage("Informe o endereço do destinatário.");
            RuleFor(x => x)
                .Must(d => !string.IsNullOrWhiteSpace(d.Documento) || !string.IsNullOrWhiteSpace(d.IdentificacaoEstrangeiro))
                .WithName("documento")
                .WithMessage("Informe o CPF/CNPJ ou a identificação do estrangeiro.");
            When(x => x.Endereco is not null, () =>
            {
                RuleFor(x => x.Endereco.Logradouro).NotEmpty().MaximumLength(60);
                RuleFor(x => x.Endereco.Numero).NotEmpty().MaximumLength(60);
                RuleFor(x => x.Endereco.Bairro).NotEmpty().MaximumLength(60);
                RuleFor(x => x.Endereco.Municipio).NotEmpty().MaximumLength(60);
                RuleFor(x => x.Endereco.Uf).NotEmpty().Length(2);
            });
        }
    }

    private sealed class PagamentoValidator : AbstractValidator<PagamentoDto>
    {
        public PagamentoValidator()
        {
            RuleFor(x => x.Detalhes).NotNull().Must(d => d.Count > 0).WithMessage("Informe ao menos uma forma de pagamento (use 'sem pagamento' quando não houver).");
            RuleFor(x => x.Troco).GreaterThanOrEqualTo(0m).When(x => x.Troco is not null);
            RuleForEach(x => x.Detalhes).ChildRules(d =>
            {
                d.RuleFor(x => x.Forma).IsInEnum();
                d.RuleFor(x => x.Indicador).IsInEnum().When(x => x.Indicador is not null);
                d.RuleFor(x => x.Valor).GreaterThanOrEqualTo(0m);
                d.RuleFor(x => x.Descricao).MaximumLength(60);
            });
        }
    }

    private sealed class ItemValidator : AbstractValidator<ItemNfeDto>
    {
        public ItemValidator()
        {
            RuleFor(x => x.Produto).NotNull();
            RuleFor(x => x.Impostos).NotNull();
            RuleFor(x => x.InformacoesAdicionais).MaximumLength(500);

            When(x => x.Produto is not null, () =>
            {
                RuleFor(x => x.Produto.Codigo).NotEmpty().MaximumLength(60);
                RuleFor(x => x.Produto.Descricao).NotEmpty().MaximumLength(120);
                RuleFor(x => x.Produto.Ncm).Matches("^[0-9]{8}$").WithMessage("NCM com 8 dígitos (use 00 para serviços conforme o layout).");
                RuleFor(x => x.Produto.Cest).Matches("^[0-9]{7}$").When(x => !string.IsNullOrWhiteSpace(x.Produto.Cest)).WithMessage("CEST com 7 dígitos.");
                RuleFor(x => x.Produto.Cfop).InclusiveBetween(1000, 7999);
                RuleFor(x => x.Produto.Unidade).NotEmpty().MaximumLength(6);
                RuleFor(x => x.Produto.UnidadeTributavel).MaximumLength(6);
                RuleFor(x => x.Produto.Quantidade).GreaterThan(0m);
                RuleFor(x => x.Produto.ValorUnitario).GreaterThanOrEqualTo(0m);
                RuleFor(x => x.Produto.ValorTotal).GreaterThanOrEqualTo(0m).When(x => x.Produto.ValorTotal is not null);
                RuleFor(x => x.Produto.Desconto).GreaterThanOrEqualTo(0m).When(x => x.Produto.Desconto is not null);
                RuleFor(x => x.Produto.CodigoBarras).Matches("^([0-9]{8}|[0-9]{12,14}|SEM GTIN)$").When(x => !string.IsNullOrWhiteSpace(x.Produto.CodigoBarras))
                    .WithMessage("GTIN com 8, 12, 13 ou 14 dígitos, ou 'SEM GTIN'.");
                RuleFor(x => x.Produto.CodigoBeneficioFiscal).Matches("^[0-9A-Za-z]{8,10}$").When(x => !string.IsNullOrWhiteSpace(x.Produto.CodigoBeneficioFiscal));
                RuleFor(x => x.Produto.NumeroPedido).MaximumLength(15);
                RuleFor(x => x.Produto.ItemPedido).InclusiveBetween(1, 999999).When(x => x.Produto.ItemPedido is not null);
            });

            When(x => x.Impostos is not null, () =>
            {
                RuleFor(x => x.Impostos.Icms).NotNull();
                RuleFor(x => x.Impostos.Pis).NotNull();
                RuleFor(x => x.Impostos.Cofins).NotNull();
                RuleFor(x => x.Impostos.Icms.Origem).IsInEnum().When(x => x.Impostos.Icms is not null);
                RuleFor(x => x.Impostos.Icms)
                    .Must(i => !string.IsNullOrWhiteSpace(i.Cst) ^ !string.IsNullOrWhiteSpace(i.Csosn))
                    .When(x => x.Impostos.Icms is not null)
                    .WithMessage("Informe CST (regime normal) ou CSOSN (Simples Nacional), nunca os dois.");
                RuleFor(x => x.Impostos.Pis.Cst).Matches("^[0-9]{2}$").When(x => x.Impostos.Pis is not null);
                RuleFor(x => x.Impostos.Cofins.Cst).Matches("^[0-9]{2}$").When(x => x.Impostos.Cofins is not null);
                RuleFor(x => x.Impostos.Ipi!.Cst).Matches("^[0-9]{2}$").When(x => x.Impostos.Ipi is not null);
                RuleFor(x => x.Impostos.IbsCbs!.Cst).Matches("^[0-9]{3}$").When(x => x.Impostos.IbsCbs is not null);
                RuleFor(x => x.Impostos.IbsCbs!.ClassificacaoTributaria).Matches("^[0-9]{6}$").When(x => x.Impostos.IbsCbs is not null)
                    .WithMessage("cClassTrib com 6 dígitos.");
            });
        }
    }
}
