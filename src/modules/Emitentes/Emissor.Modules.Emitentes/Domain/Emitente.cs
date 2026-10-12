using Emissor.BuildingBlocks;
using Emissor.Contracts.Emitentes;

namespace Emissor.Modules.Emitentes.Domain;

/// <summary>
/// Quem assina e emite a nota: um CPF de produtor rural com Inscricao Estadual
/// ou um CNPJ. Pertence a um tenant. Os dados aqui vao direto para o grupo
/// <c>emit</c> da NF-e, por isso as validacoes seguem o layout.
/// </summary>
public sealed class Emitente : ITenantOwned
{
    public const int NomeMaxLength = 60;
    public const int NomeFantasiaMaxLength = 60;
    public const int InscricaoEstadualMaxLength = 14;
    public const int EmailMaxLength = 60;

    private Emitente() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public TipoEmitente Tipo { get; private set; }

    /// <summary>CPF (11) ou CNPJ (14), so os caracteres significativos, sem mascara.</summary>
    public string Documento { get; private set; } = string.Empty;

    public string InscricaoEstadual { get; private set; } = string.Empty;
    public string Nome { get; private set; } = string.Empty;
    public string? NomeFantasia { get; private set; }
    public RegimeTributario RegimeTributario { get; private set; }
    public AmbienteSefaz Ambiente { get; private set; }
    public Endereco Endereco { get; private set; } = null!;
    public string? Email { get; private set; }
    public bool Ativo { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset AtualizadoEm { get; private set; }

    public Certificado? Certificado { get; private set; }

    /// <summary>Contingencia SVC-AN ligada: as notas saem com tpEmis 6 e vao para a Sefaz Virtual de Contingencia.</summary>
    public DateTimeOffset? ContingenciaDesde { get; private set; }
    public string? ContingenciaJustificativa { get; private set; }
    public bool EmContingencia => ContingenciaDesde is not null;

    public const int JustificativaContingenciaMin = 15;
    public const int JustificativaContingenciaMax = 256;

    /// <summary>
    /// Entrar em contingencia e decisao de quem opera: o SVRS caiu (108/109 ou
    /// rede) e a venda nao pode esperar. A SEFAZ autorizadora precisa ter
    /// ativado a SVC para a UF, senao a SVC rejeita.
    /// </summary>
    public Result AtivarContingencia(string? justificativa, DateTimeOffset agora)
    {
        var texto = string.Join(' ', (justificativa ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (texto.Length is < JustificativaContingenciaMin or > JustificativaContingenciaMax)
            return Result.Failure(EmitenteErrors.JustificativaContingenciaInvalida);

        if (ContingenciaDesde is null)
            ContingenciaDesde = agora;

        ContingenciaJustificativa = texto;
        AtualizadoEm = agora;

        return Result.Success();
    }

    public void DesativarContingencia(DateTimeOffset agora)
    {
        ContingenciaDesde = null;
        ContingenciaJustificativa = null;
        AtualizadoEm = agora;
    }

    public sealed record Dados(
        TipoEmitente Tipo,
        string? Documento,
        string? InscricaoEstadual,
        string? Nome,
        string? NomeFantasia,
        RegimeTributario RegimeTributario,
        AmbienteSefaz Ambiente,
        Endereco Endereco,
        string? Email);

    public static Result<Emitente> Criar(Guid tenantId, Dados dados, DateTimeOffset agora)
    {
        var emitente = new Emitente
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Ativo = true,
            CriadoEm = agora
        };

        var aplicado = emitente.Aplicar(dados, agora);

        return aplicado.IsFailure ? aplicado.FailAs<Emitente>() : Result<Emitente>.Success(emitente);
    }

    public Result Atualizar(Dados dados, DateTimeOffset agora)
    {
        // Trocar o documento de um emitente que ja tem certificado deixaria o
        // certificado de outro titular guardado nele.
        var documentoNovo = DocumentoFiscal.SomenteCaracteresValidos(dados.Documento);

        if (Certificado is not null && documentoNovo != Documento)
            return Result.Failure(EmitenteErrors.DocumentoComCertificado);

        return Aplicar(dados, agora);
    }

    private Result Aplicar(Dados dados, DateTimeOffset agora)
    {
        var documento = DocumentoFiscal.SomenteCaracteresValidos(dados.Documento);

        var documentoValido = dados.Tipo switch
        {
            TipoEmitente.PessoaFisica => DocumentoFiscal.IsCpfValido(documento),
            TipoEmitente.PessoaJuridica => DocumentoFiscal.IsCnpjValido(documento),
            _ => false
        };

        if (!documentoValido)
            return Result.Failure(dados.Tipo == TipoEmitente.PessoaFisica ? EmitenteErrors.CpfInvalido : EmitenteErrors.CnpjInvalido);

        // Emitente de NF-e tem IE obrigatoria: o produtor rural PF so emite com
        // a inscricao vinculada ao CPF. "ISENTO" e coisa de destinatario.
        var ie = Endereco.SomenteDigitos(dados.InscricaoEstadual);

        if (ie.Length is < 2 or > InscricaoEstadualMaxLength)
            return Result.Failure(EmitenteErrors.InscricaoEstadualInvalida);

        var nome = Endereco.Limpar(dados.Nome);

        if (nome.Length is < 2 or > NomeMaxLength)
            return Result.Failure(EmitenteErrors.NomeInvalido);

        var nomeFantasia = Endereco.Limpar(dados.NomeFantasia);

        if (nomeFantasia.Length > NomeFantasiaMaxLength)
            return Result.Failure(EmitenteErrors.NomeFantasiaInvalido);

        if (!Enum.IsDefined(dados.RegimeTributario))
            return Result.Failure(EmitenteErrors.RegimeTributarioInvalido);

        if (!Enum.IsDefined(dados.Ambiente))
            return Result.Failure(EmitenteErrors.AmbienteInvalido);

        var email = Endereco.Limpar(dados.Email);

        if (email.Length > EmailMaxLength || (email.Length > 0 && !email.Contains('@')))
            return Result.Failure(EmitenteErrors.EmailInvalido);

        Tipo = dados.Tipo;
        Documento = documento;
        InscricaoEstadual = ie;
        Nome = nome;
        NomeFantasia = nomeFantasia.Length == 0 ? null : nomeFantasia;
        RegimeTributario = dados.RegimeTributario;
        Ambiente = dados.Ambiente;
        Endereco = dados.Endereco;
        Email = email.Length == 0 ? null : email;
        AtualizadoEm = agora;

        return Result.Success();
    }

    public void Desativar(DateTimeOffset agora)
    {
        Ativo = false;
        AtualizadoEm = agora;
    }

    public void Reativar(DateTimeOffset agora)
    {
        Ativo = true;
        AtualizadoEm = agora;
    }

    /// <summary>
    /// Guarda (ou troca) o certificado. O titular do certificado tem que ser o
    /// proprio emitente: um e-CNPJ de outra empresa assinaria a nota em nome
    /// errado e a SEFAZ rejeita (ou pior, autoriza com assinatura de terceiro).
    /// </summary>
    public Result DefinirCertificado(Certificado certificado, DateTimeOffset agora)
    {
        if (certificado.Documento != Documento)
            return Result.Failure(EmitenteErrors.CertificadoDeOutroTitular);

        if (certificado.ValidoAte <= agora)
            return Result.Failure(EmitenteErrors.CertificadoVencido);

        Certificado = certificado;
        AtualizadoEm = agora;

        return Result.Success();
    }

    public void RemoverCertificado(DateTimeOffset agora)
    {
        Certificado = null;
        AtualizadoEm = agora;
    }
}
