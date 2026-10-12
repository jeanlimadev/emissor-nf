using Emissor.BuildingBlocks;

namespace Emissor.Modules.Emitentes.Domain;

public static class EmitenteErrors
{
    public static readonly Error NaoEncontrado =
        Error.NotFound("emitente.nao_encontrado", "Emitente não encontrado.");

    public static readonly Error CpfInvalido =
        Error.Validation("emitente.cpf_invalido", "O CPF informado não é válido.");

    public static readonly Error CnpjInvalido =
        Error.Validation("emitente.cnpj_invalido", "O CNPJ informado não é válido.");

    public static readonly Error DocumentoDuplicado =
        Error.Conflict("emitente.documento_duplicado", "Já existe um emitente com este CPF/CNPJ neste tenant.");

    public static readonly Error DocumentoComCertificado =
        Error.Conflict("emitente.documento_com_certificado", "Remova o certificado antes de trocar o CPF/CNPJ do emitente.");

    public static readonly Error InscricaoEstadualInvalida =
        Error.Validation("emitente.ie_invalida", $"Informe a Inscrição Estadual com até {Emitente.InscricaoEstadualMaxLength} dígitos. O emitente de NF-e precisa de IE.");

    public static readonly Error NomeInvalido =
        Error.Validation("emitente.nome_invalido", $"Informe o nome ou razão social com até {Emitente.NomeMaxLength} caracteres.");

    public static readonly Error NomeFantasiaInvalido =
        Error.Validation("emitente.nome_fantasia_invalido", $"O nome fantasia deve ter até {Emitente.NomeFantasiaMaxLength} caracteres.");

    public static readonly Error RegimeTributarioInvalido =
        Error.Validation("emitente.crt_invalido", "Informe o regime tributário (CRT 1 a 4).");

    public static readonly Error JustificativaContingenciaInvalida =
        Error.Validation("emitente.justificativa_contingencia_invalida", $"A justificativa da contingência deve ter entre {Emitente.JustificativaContingenciaMin} e {Emitente.JustificativaContingenciaMax} caracteres.");

    public static readonly Error AmbienteInvalido =
        Error.Validation("emitente.ambiente_invalido", "Informe o ambiente: 1 produção ou 2 homologação.");

    public static readonly Error EmailInvalido =
        Error.Validation("emitente.email_invalido", $"Informe um e-mail válido com até {Emitente.EmailMaxLength} caracteres.");

    public static readonly Error Inativo =
        Error.Validation("emitente.inativo", "O emitente está desativado.");

    public static readonly Error CertificadoNaoEncontrado =
        Error.NotFound("certificado.nao_encontrado", "O emitente não tem certificado cadastrado.");

    public static readonly Error CertificadoIlegivel =
        Error.Validation("certificado.ilegivel", "Não foi possível abrir o arquivo: confira se é um .pfx/.p12 válido e se a senha está correta.");

    public static readonly Error CertificadoSemChavePrivada =
        Error.Validation("certificado.sem_chave_privada", "O certificado não contém a chave privada. Exporte o .pfx com a chave.");

    public static readonly Error CertificadoSemDocumento =
        Error.Validation("certificado.sem_documento", "Não foi possível ler o CPF/CNPJ do titular no certificado. Ele precisa ser um e-CPF ou e-CNPJ ICP-Brasil.");

    public static readonly Error CertificadoDeOutroTitular =
        Error.Validation("certificado.outro_titular", "O CPF/CNPJ do certificado não é o do emitente.");

    public static readonly Error CertificadoVencido =
        Error.Validation("certificado.vencido", "O certificado está vencido.");

    public static readonly Error CertificadoMuitoGrande =
        Error.Validation("certificado.muito_grande", "O arquivo do certificado é maior que o esperado para um A1.");

    public static readonly Error SerieNaoEncontrada =
        Error.NotFound("serie.nao_encontrada", "Série não encontrada.");

    public static readonly Error SerieInvalida =
        Error.Validation("serie.numero_invalido", $"A série deve estar entre {Serie.NumeroMin} e {Serie.NumeroMax}.");

    public static readonly Error SerieDuplicada =
        Error.Conflict("serie.duplicada", "O emitente já tem esta série.");

    public static readonly Error SerieInativa =
        Error.Validation("serie.inativa", "A série está desativada.");

    public static readonly Error SerieEsgotada =
        Error.Validation("serie.esgotada", "A numeração da série chegou ao fim. Crie outra série.");

    public static readonly Error ProximoNumeroInvalido =
        Error.Validation("serie.proximo_numero_invalido", $"O próximo número deve estar entre 1 e {Serie.NumeroNotaMax}.");

    public static readonly Error ProximoNumeroRetrocede =
        Error.Validation("serie.proximo_numero_retrocede", "O próximo número não pode ser menor que o atual: número já usado não volta.");
}
