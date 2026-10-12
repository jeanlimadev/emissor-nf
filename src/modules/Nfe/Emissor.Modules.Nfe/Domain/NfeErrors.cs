using Emissor.BuildingBlocks;
using Emissor.Contracts.Nfe;

namespace Emissor.Modules.Nfe.Domain;

public static class NfeErrors
{
    // Chave de acesso
    public static readonly Error ChaveInvalida =
        Error.Validation("nfe.chave_invalida", "A chave de acesso deve ter 44 dígitos.");

    public static readonly Error ChaveDigitoVerificadorInvalido =
        Error.Validation("nfe.chave_dv_invalido", "O dígito verificador da chave de acesso não confere.");

    public static readonly Error ChaveUfInvalida =
        Error.Validation("nfe.chave_uf_invalida", "Código de UF inválido para a chave de acesso.");

    public static readonly Error ChaveDocumentoInvalido =
        Error.Validation("nfe.chave_documento_invalido", "O documento do emitente precisa ser um CPF (11 dígitos) ou CNPJ (14 posições; letras maiúsculas só nas 12 primeiras).");

    public static readonly Error ChaveSerieInvalida =
        Error.Validation("nfe.chave_serie_invalida", "A série deve estar entre 0 e 999.");

    public static readonly Error ChaveNumeroInvalido =
        Error.Validation("nfe.chave_numero_invalido", "O número da nota deve estar entre 1 e 999.999.999.");

    public static readonly Error ChaveCodigoNumericoInvalido =
        Error.Validation("nfe.chave_cnf_invalido", "O código numérico (cNF) deve ter 8 dígitos.");

    public static readonly Error ChaveCodigoNumericoIgualAoNumero =
        Error.Validation("nfe.chave_cnf_igual_numero", "O código numérico (cNF) não pode ser igual ao número da nota.");

    // Pedido de emissao
    public static readonly Error PedidoInvalido =
        Error.Validation("nfe.pedido_invalido", "O pedido de emissão tem campos inválidos. Veja a lista em `errors`.");

    public static readonly Error EmitenteNaoEncontrado =
        Error.NotFound("nfe.emitente_nao_encontrado", "Emitente não encontrado.");

    public static readonly Error EmitenteInativo =
        Error.Validation("nfe.emitente_inativo", "O emitente está desativado e não pode emitir.");

    public static readonly Error EmitenteSemCertificado =
        Error.Validation("nfe.emitente_sem_certificado", "O emitente não tem certificado A1 cadastrado.");

    public static readonly Error CertificadoVencido =
        Error.Validation("nfe.certificado_vencido", "O certificado do emitente está vencido.");

    public static readonly Error CertificadoIlegivel =
        Error.Unavailable("nfe.certificado_ilegivel", "Não foi possível abrir o certificado guardado do emitente. Envie o certificado novamente.");

    public static readonly Error SerieNaoEncontrada =
        Error.Validation("nfe.serie_nao_encontrada", "A série informada não existe para este emitente.");

    public static readonly Error SerieInativa =
        Error.Validation("nfe.serie_inativa", "A série informada está desativada.");

    public static readonly Error XmlInvalido =
        Error.Validation("nfe.xml_invalido", "O XML gerado não passou na validação do schema da NF-e. Veja a lista em `errors`.");

    // Emissao e fila
    public static readonly Error NaoEncontrada =
        Error.NotFound("nfe.nao_encontrada", "Nota não encontrada.");

    public static readonly Error IdempotenciaAusente =
        Error.Validation("nfe.idempotencia_ausente", $"Informe o header Idempotency-Key (até {NotaFiscal.ChaveIdempotenciaMaxLength} caracteres) para a emissão não ser duplicada em caso de repetição.");

    public static readonly Error IdempotenciaConflito =
        Error.Conflict("nfe.idempotencia_conflito", "Já existe uma nota com esta Idempotency-Key, mas com um pedido diferente. Use outra chave para um pedido novo.");

    public static readonly Error ReenvioNaoPermitido =
        Error.Conflict("nfe.reenvio_nao_permitido", "Só notas rejeitadas, com falha ou pendentes de consulta podem ser reenviadas.");

    public static readonly Error ReenvioPendenteComPedido =
        Error.Conflict("nfe.reenvio_pendente_com_pedido", "A nota está pendente de consulta e pode já constar na SEFAZ: o reenvio sem corpo antecipa a próxima tentativa, mas o pedido não pode ser alterado.");

    public static readonly Error XmlIndisponivel =
        Error.NotFound("nfe.xml_indisponivel", "A nota ainda não tem XML: ela não foi processada.");

    public static readonly Error DanfeIndisponivel =
        Error.Conflict("nfe.danfe_indisponivel", "A DANFE só pode ser gerada para nota autorizada (ou denegada/cancelada, com a marca correspondente). Para conferir antes, use a prévia em PDF.");

    public static readonly Error SefazIndisponivel =
        Error.Unavailable("nfe.sefaz_indisponivel", "A SEFAZ não respondeu. Tente novamente em instantes.");

    // Webhook
    public static readonly Error WebhookUrlInvalida =
        Error.Validation("webhook.url_invalida", "Informe uma URL https absoluta (http só para localhost), com até 500 caracteres.");

    public static readonly Error WebhookNaoConfigurado =
        Error.NotFound("webhook.nao_configurado", "Este tenant não tem webhook configurado.");

    public static readonly Error WebhookSegredoInvalido =
        Error.Validation("webhook.segredo_invalido", "O segredo deve ter entre 16 e 128 caracteres.");

    // Eventos e inutilizacao
    public static readonly Error CancelamentoSoDeAutorizada =
        Error.Conflict("nfe.cancelamento_so_de_autorizada", "Só uma nota autorizada pode ser cancelada.");

    public static readonly Error CancelamentoForaDoPrazo =
        Error.Conflict("nfe.cancelamento_fora_do_prazo", "O prazo de cancelamento (24 horas após a autorização) já passou.");

    public static readonly Error CancelamentoNaoRegistrado =
        Error.Conflict("nfe.cancelamento_nao_registrado", "O evento de cancelamento não foi registrado pela SEFAZ.");

    public static readonly Error JustificativaInvalida =
        Error.Validation("nfe.justificativa_invalida", $"A justificativa deve ter entre {EventoNfe.JustificativaMin} e {EventoNfe.JustificativaMax} caracteres.");

    public static readonly Error CartaSoDeAutorizada =
        Error.Conflict("nfe.carta_so_de_autorizada", "Só uma nota autorizada (e não cancelada) recebe carta de correção.");

    public static readonly Error CartaLimiteAtingido =
        Error.Conflict("nfe.carta_limite_atingido", $"A nota já tem {EventoNfe.MaxCartasCorrecao} cartas de correção, o máximo permitido.");

    public static readonly Error CorrecaoInvalida =
        Error.Validation("nfe.correcao_invalida", $"O texto da correção deve ter entre {EventoNfe.CorrecaoMin} e {EventoNfe.CorrecaoMax} caracteres.");

    public static readonly Error EventoNaoEncontrado =
        Error.NotFound("nfe.evento_nao_encontrado", "Evento não encontrado.");

    public static readonly Error EventoEmAndamento =
        Error.Conflict("nfe.evento_em_andamento", "Já existe um evento deste tipo pendente para a nota. Repita a chamada para concluí-lo.");

    public static readonly Error InutilizacaoSoParaCnpj =
        Error.Validation("nfe.inutilizacao_so_para_cnpj", "O layout da inutilização (inutNFe 4.00) só aceita emitente com CNPJ.");

    public static readonly Error FaixaInvalida =
        Error.Validation("nfe.faixa_invalida", "Informe uma faixa válida: número inicial a partir de 1 e número final maior ou igual ao inicial.");

    public static readonly Error FaixaComNotaEmitida =
        Error.Conflict("nfe.faixa_com_nota_emitida", "Há nota emitida (ou em processamento) com número dentro da faixa. Só numeração nunca usada pode ser inutilizada.");

    public static readonly Error InutilizacaoEmContingencia =
        Error.Conflict("nfe.inutilizacao_em_contingencia", "A SVC-AN não oferece inutilização. Desligue a contingência do emitente para inutilizar.");

    public static readonly Error InutilizacaoNaoEncontrada =
        Error.NotFound("nfe.inutilizacao_nao_encontrada", "Inutilização não encontrada.");

    public static Error TransicaoInvalida(NfeStatus de, NfeStatus para) =>
        Error.Conflict("nfe.transicao_invalida", $"A nota está {de} e não pode ir para {para}.");
}
