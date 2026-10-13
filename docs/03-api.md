# API (v1)

Autenticação dos sistemas clientes: header `X-Api-Key: emk_...`. Administração do serviço:
header `X-Admin-Token`. Erros vêm em `ProblemDetails` com `code` estável.

## Administração (`X-Admin-Token`)

| Método | Rota | O que faz |
|---|---|---|
| POST | `/api/v1/admin/tenants` | Cria um tenant. Corpo: `{ "nome" }`. |
| GET | `/api/v1/admin/tenants?page=&pageSize=` | Lista tenants (paginado). |
| POST | `/api/v1/admin/tenants/{tenantId}/api-keys` | Cria chave. A resposta traz `chave` em texto **uma única vez**. |
| GET | `/api/v1/admin/tenants/{tenantId}/api-keys` | Lista chaves (só metadados). |
| DELETE | `/api/v1/admin/tenants/{tenantId}/api-keys/{apiKeyId}` | Revoga a chave. |

## Tenant (`X-Api-Key`)

| Método | Rota | O que faz |
|---|---|---|
| GET | `/api/v1/me` | Qual tenant a chave abre. |
| POST | `/api/v1/emitentes` | Cadastra emitente (`EmitenteRequest`). |
| GET | `/api/v1/emitentes?page=&pageSize=&ativo=` | Lista emitentes do tenant. |
| GET | `/api/v1/emitentes/{id}` | Consulta. |
| PUT | `/api/v1/emitentes/{id}` | Atualiza. Com certificado cadastrado, o documento não muda. |
| POST | `/api/v1/emitentes/{id}/desativar` · `/reativar` | Liga/desliga o emitente. |
| PUT | `/api/v1/emitentes/{id}/certificado` | Multipart: `arquivo` (.pfx) + `senha`. Devolve metadados. |
| GET | `/api/v1/emitentes/{id}/certificado` | Metadados (titular, documento, validade). |
| DELETE | `/api/v1/emitentes/{id}/certificado` | Remove. |
| POST | `/api/v1/emitentes/{id}/series` | Cria série: `{ "numero": 1, "proximoNumero": 1 }`. |
| GET | `/api/v1/emitentes/{id}/series` | Lista séries. |
| PUT | `/api/v1/emitentes/{id}/series/{serieId}` | `{ "proximoNumero"?, "ativa"? }`. Próximo número nunca retrocede. |
| POST | `/api/v1/emitentes/{id}/nfe` | **Emite.** Header `Idempotency-Key` obrigatório. Valida, enfileira e responde `202` com `NfeResponse`; com `?aguardar=N` (até 60 s) espera o resultado da SEFAZ e responde `200` quando a nota chega a um status final. |
| GET | `/api/v1/emitentes/{id}/sefaz/status` | Status do autorizador (NfeStatusServico4) com o certificado do emitente. `107` = em operação. |
| GET | `/api/v1/nfe?emitenteId=&status=&page=&pageSize=` | Lista notas do tenant, da mais nova para a mais antiga. |
| GET | `/api/v1/nfe/{id}` | Situação da nota: status, número, chave, protocolo, `cStat`, `xMotivo`, tentativas. |
| GET | `/api/v1/nfe/{id}/danfe` | DANFE em PDF (A4 retrato) da nota autorizada. Denegada/cancelada saem com a marca; outras situações respondem `409 nfe.danfe_indisponivel`. |
| POST | `/api/v1/emitentes/{id}/nfe/previa/danfe` | DANFE em PDF da prévia, com "PRÉVIA - SEM VALOR FISCAL". Mesmo corpo da prévia. |
| GET | `/api/v1/nfe/{id}/xml` | `procNFe` (nota + protocolo) quando autorizada ou denegada; senão o XML assinado. |
| POST | `/api/v1/nfe/{id}/cancelamento` | Cancela nota autorizada (evento 110111) em até 24 h da autorização. Corpo `{ "justificativa" }` (15 a 255). Síncrono: a resposta traz o `cStat` do evento (135 registrado). A nota vira `Cancelada` e dispara `nfe.cancelada`. |
| POST | `/api/v1/nfe/{id}/carta-correcao` | Carta de correção (evento 110110). Corpo `{ "correcao" }` (15 a 1000). Cada carta substitui as anteriores; até 20 por nota. Dispara `nfe.carta_correcao`. |
| GET | `/api/v1/nfe/{id}/eventos` | Eventos da nota com status, sequência, protocolo e motivo. |
| GET | `/api/v1/nfe/{id}/eventos/{eventoId}/xml` | `procEventoNFe` (evento + retorno) quando registrado. |
| POST | `/api/v1/emitentes/{id}/contingencia` | Liga a contingência SVC-AN: `{ "justificativa" }` (15 a 256). Próximas notas saem com `tpEmis 6`, `dhCont` e `xJust`, e vão para a Sefaz Virtual de Contingência. |
| DELETE | `/api/v1/emitentes/{id}/contingencia` | Desliga a contingência. |
| POST | `/api/v1/emitentes/{id}/inutilizacoes` | Inutiliza faixa nunca usada: `{ "serie", "numeroInicial", "numeroFinal", "justificativa" }`. Só emitente CNPJ (o layout `inutNFe` não tem CPF). `cStat` 102 = homologada; a série pula a faixa. |
| GET | `/api/v1/emitentes/{id}/inutilizacoes` | Inutilizações do emitente. |
| GET | `/api/v1/emitentes/{id}/inutilizacoes/{inutilizacaoId}/xml` | `procInutNFe`. |
| POST | `/api/v1/nfe/{id}/reenviar` | Nota rejeitada ou com falha volta para a fila. Corpo opcional: pedido corrigido. Número e série são mantidos; chave e XML são refeitos. Nota pendente de consulta aceita só sem corpo: antecipa a próxima tentativa. Rejeitada por **539** (número já usado por outra nota) ganha número novo no reenvio; rejeitada por **204** (a SEFAZ já tem esta chave) volta para consulta em vez de retransmitir. |
| PUT | `/api/v1/webhook` | Configura o webhook do tenant: `{ "url", "segredo"? }`. Sem segredo, o emissor gera um e o devolve **uma única vez**. URL https (http só em localhost). |
| GET | `/api/v1/webhook` | Webhook configurado (sem o segredo). |
| DELETE | `/api/v1/webhook` | Remove o webhook. |
| POST | `/api/v1/webhook/testar` | Enfileira um evento `webhook.teste`. |
| GET | `/api/v1/webhook/entregas?nfeId=&page=&pageSize=` | Entregas: status, tentativas, último HTTP e erro. |
| POST | `/api/v1/emitentes/{id}/nfe/previa` | Monta, assina e valida a NF-e contra o schema **sem transmitir e sem consumir número**. Corpo: `EmissaoNfeRequest`. Resposta: `NfePreviaResponse` (chave, série, número, XML assinado); com `?formato=xml`, só o XML (`application/xml`). |
| GET | `/health` | Banco de pé? |

Operação (variáveis, deploy, rotina e problemas) em `06-operacao.md`.

## Ciclo de vida da nota (`status`)

```text
Recebida ──► EmProcessamento ──► Autorizada            (cStat 100; tem protocolo e procNFe)
                 │           ├──► Rejeitada            (cStat de rejeição; reenvio corrigido mantém o número)
                 │           ├──► Denegada             (cStat 110/301/302/303; número consumido)
                 │           └──► PendenteConsulta ──┐ (sem resposta conclusiva: rede, lote 103/105, serviço 108/109)
                 │                                   │  o worker consulta pela chave antes de reenviar
                 └◄──────────────────────────────────┘
Rejeitada/Falha ──(reenviar)──► Recebida
Falha: o emissor desistiu (erro repetido ou pedido que deixou de ser válido); `xMotivo` explica.
```

Regras de transmissão:

- A nota só recebe número quando o worker a pega: a numeração é consumida em ordem de processamento,
  por um `UPDATE ... RETURNING` atômico na série.
- Antes de qualquer reenvio (segunda tentativa em diante), o worker consulta a SEFAZ pela chave. Se a nota
  já consta (100), ela é dada como autorizada sem retransmitir; se não consta (217), retransmite o mesmo XML.
- Espera entre tentativas: 30 s, dobrando até 30 min; depois de 12 tentativas a nota vai para `Falha`.
- Nota presa em `EmProcessamento` por mais de 5 min (processo que caiu) é retomada por qualquer instância.
- `Idempotency-Key` igual com o mesmo pedido devolve a nota existente; com pedido diferente, `409`.

## Pedido de emissão (`EmissaoNfeRequest`)

Os DTOs estão em `src/contracts/Emissor.Contracts/Nfe/NfeDtos.cs`. Os enums vão como número e
valem exatamente os códigos do layout 4.00 (`tipoOperacao` 1 = saída, `presenca` 1 = presencial,
`formaPagamento` 17 = PIX dinâmico, e assim por diante).

```text
identificacao     serie, naturezaOperacao, tipoOperacao, finalidade, consumidorFinal, presenca,
                  dataEmissao?, dataSaidaEntrada?, notasReferenciadas?[]
destinatario      documento (CPF/CNPJ) ou identificacaoEstrangeiro, nome, indicadorIe,
                  inscricaoEstadual?, endereco, email?
itens[]           produto { codigo, codigoBarras?, descricao, ncm, cest?, cfop, unidade, quantidade,
                            valorUnitario, valorTotal?, frete?, seguro?, desconto?, outrasDespesas?, ... }
                  impostos { icms, pis, cofins, ipi?, ibsCbs?, valorAproximadoTributos? }
                  informacoesAdicionais?
transporte        modalidadeFrete, transportador?, volumes?[]
pagamento         detalhes[] { indicador?, forma, valor, descricao? }, troco?
informacoesAdicionais?  fisco?, contribuinte?
```

Regras que o emissor aplica antes de montar o XML (a SEFAZ rejeitaria depois):

- Regime normal usa `icms.cst`; Simples Nacional usa `icms.csosn`. Cada CST/CSOSN exige um
  subconjunto de campos; o que faltar vem listado em `errors` com o caminho do campo
  (`itens[0].impostos.icms.baseCalculo`).
- `valorTotal` do item, quando informado, tem que bater com quantidade x valor unitário (tolerância
  de 0,01). Vazio, o emissor calcula.
- Os totais (`ICMSTot`, `IBSCBSTot`) são **somados** pelo emissor a partir dos itens; o cliente não
  os envia. `vNF` segue a fórmula do layout.
- A soma dos pagamentos menos o troco tem que fechar com `vNF` (salvo forma 90, sem pagamento).
- Em homologação o nome do destinatário é trocado pelo texto fixo que a SEFAZ exige.
- `ibsCbs` é opcional por item (Simples Nacional entra na Reforma em 2027). Informando base ou
  qualquer tributo, os grupos `ibsUf`, `ibsMunicipal` e `cbs` passam a ser obrigatórios.

### Erros de validação

`400` com `code: nfe.pedido_invalido` e a extensão `errors` no formato
`{ "campo.em.camelCase": ["mensagem", ...] }`. O mesmo formato vale para `nfe.xml_invalido`
(falha no schema, chave `xml`).

## Limite de requisições

Cada chave de API tem uma cota de requisições por minuto (`RateLimit:PorMinuto`, padrão 300). Acima
disso a resposta é `429` com `code: requisicao.limite_atingido`. Chamadas sem chave (admin, health)
contam por endereço IP.

## Cliente tipado

O pacote `Emissor.Contracts` traz, além dos DTOs, o `EmissorClient`: um cliente HTTP com um método por
endpoint que devolve `Resposta<T>` (valor ou `Problema` com `code`, `status` e `errors`). Só depende da
BCL. O documento OpenAPI fica em `/swagger/v1/swagger.json` em todo ambiente, para gerar clientes em
outras linguagens.

## Eventos: comportamento quando a SEFAZ não responde

Cancelamento, carta de correção e inutilização são síncronos. Se a chamada à SEFAZ falhar (rede,
timeout, HTTP 5xx), a resposta é `503 nfe.sefaz_indisponivel` e o evento fica `Pendente` com o
mesmo XML assinado. **Repita a mesma chamada**: o emissor retoma o evento pendente (mesma
sequência, mesmo XML) em vez de criar um segundo. A inutilização pendente aparece na lista; se não
constar na SEFAZ, refaça o pedido.

Regras aplicadas antes de transmitir: cancelamento só de nota `Autorizada` e dentro de 24 h;
carta só de nota `Autorizada` (cancelada não recebe); inutilização só de faixa sem nenhuma nota
do emitente (qualquer status com número atribuído) e só para emitente CNPJ. A IE do destinatário
tem o dígito conferido para o RJ antes de montar o XML (`destinatario.inscricaoEstadual` em `errors`).

## Webhook

A cada status final de nota (`Autorizada`, `Rejeitada`, `Denegada`, `Falha`, e `Cancelada` na fase 4)
o emissor faz um `POST` na URL configurada, com corpo JSON e estes headers:

| Header | Conteúdo |
|---|---|
| `X-Emissor-Event` | `nfe.autorizada`, `nfe.rejeitada`, `nfe.denegada`, `nfe.falha`, `nfe.cancelada`, `nfe.carta_correcao`, `emitente.certificado_vencendo` (diário, a partir de 30 dias antes; traz `certificado` no corpo) ou `webhook.teste` |
| `X-Emissor-Delivery` | Id da entrega. Use para ignorar repetições: a mesma entrega pode chegar mais de uma vez. |
| `X-Emissor-Timestamp` | Unix time (segundos) do envio. |
| `X-Emissor-Signature` | `sha256=<hex>`: HMAC-SHA256 do corpo (bytes exatos) com o segredo. |

Corpo: `{ "evento", "entregaId", "ocorridoEm", "nota": NfeResponse | null, "certificado": CertificadoVencendoDto | null }`. Responda `2xx` em
até 10 s. Qualquer outra resposta (ou timeout) é retentada com espera crescente (30 s, 1 min, 2 min...
até 30 min), dez vezes; depois a entrega fica `Falha` e aparece em `/webhook/entregas`.

Conferindo a assinatura em C#:

```csharp
var esperada = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(segredoBytes, corpoBytes));
var ok = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(esperada), Encoding.ASCII.GetBytes(headerSignature));
```

## Paginação

Toda listagem responde `{ items, page, pageSize, total }`. Sem `page` e `pageSize`, devolve
tudo; `pageSize` acima de 200 é recusado com 400.

## Códigos de erro já existentes

`tenant.*`, `api_key.*`, `emitente.*`, `endereco.*`, `certificado.*`, `serie.*`, `nfe.*`, `webhook.*`, `paging.*`.
A lista completa está nas classes `TenancyErrors`, `EmitenteErrors` e `NfeErrors`.

Corpo que não é JSON, JSON malformado ou campo com tipo errado respondem `400` com
`code: requisicao.corpo_invalido` e o ponto exato da quebra em `detail`.
