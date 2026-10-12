# Arquitetura

## Direção geral

```text
Sistema cliente (ERP, outros)
  → HTTPS + X-Api-Key
    → Emissor.Api (Minimal APIs, .NET 10)
      → caso de uso do módulo (Result Pattern)
        → domínio
          → portas (ISefazGateway, INfeXmlBuilder, ICertificadoProtector, ...)
            → adaptadores: EF Core/PostgreSQL, DFe.NET, QuestPDF
      → Worker em background (fila no Postgres, SKIP LOCKED)
```

Princípios, herdados do `erp_flv`: organização por módulo e caso de uso; regras de negócio fora
de endpoints; dependências externas atrás de contratos; abstrações só onde protegem uma
fronteira real.

## Módulos

| Módulo | Schema | Responsabilidade |
|---|---|---|
| `Tenancy` | `tenancy` | Tenants, API keys (hash), autenticação (`ApiKey` e `Admin`), `ICurrentTenant`. |
| `Emitentes` | `emitentes` | Cadastro do emitente (grupo `emit`), certificado A1 criptografado, séries de numeração. |
| `Nfe` | `nfe` | Emissão, transmissão, consulta, eventos, DANFE, webhooks. (fases 1 a 4) |

`Emissor.BuildingBlocks` tem só o transversal: `Result`/`Error`, paginação, `ICurrentTenant`,
`IClock`, `ProblemResults`, `DocumentoFiscal`, `SecretRedactor`.
`Emissor.Contracts` tem os DTOs públicos da API, sem dependências, para virar pacote NuGet que
o ERP consome.

## Multi-tenancy

- O tenant vem da API key, nunca do corpo. `ApiKeyAuthenticationHandler` resolve a chave e põe
  `tenant_id` nas claims; `ICurrentTenant` lê de lá. Fora de uma requisição (worker), o escopo
  amarra um `AmbientTenant` uma única vez.
- Toda entidade de negócio implementa `ITenantOwned`, e o `DbContext` do módulo aplica
  `HasQueryFilter(e => e.TenantId == TenantId)`. Consulta sem tenant amarrado falha, não vaza.
- Unicidade é por tenant (o mesmo CPF pode ser emitente em dois tenants).

## Certificados

- Upload por multipart (`arquivo` + `senha`). O serviço abre o pfx, exige chave privada, lê o
  CPF/CNPJ do titular (SAN ICP-Brasil, senão o CN `NOME:DOCUMENTO`), confere com o emitente e
  recusa vencido.
- Guardado com AES-256-GCM (`versao | nonce | tag | cifrado`) usando a chave mestra
  `Certificados:ChaveMestra` (32 bytes em base64, vinda do ambiente). A senha é guardada do
  mesmo jeito. Nada disso aparece em log ou resposta; a API só devolve metadados.
- Pfx antigo (PKCS#12 com RC2) pode falhar no OpenSSL do Linux: re-exportar o certificado.

## Numeração

`Serie` guarda `ProximoNumero`. A reserva (fase 2) faz `SELECT ... FOR UPDATE` na linha da
série dentro da mesma transação que grava a nota. Rejeição permite reaproveitar o número no
reenvio da mesma nota; nota abandonada vira inutilização.

## Erros

Falha esperada é `Result`/`Error` com `Kind` (`Validation`, `NotFound`, `Conflict`, ...).
`ProblemResults.FromError` traduz para `ProblemDetails` com o código em `extensions.code`.
Exceção é para o inesperado e vira 500 pelo `UseExceptionHandler`.

## O que evitar

- Lógica de negócio em endpoint ou em `DbContext`.
- Expor classes da DFe.NET fora de `Nfe/Infrastructure`.

## Caminho offline da NF-e (módulo Nfe, fase 1)

```text
EmissaoNfeRequest
  → EmissaoNfeRequestValidator (FluentValidation: estrutura, tamanhos, formatos)
  → ChaveAcesso.Criar (cUF, AAMM, documento, série, número, cNF, DV)
  → INfeXmlBuilder (DfeNetNfeXmlBuilder: pedido → classes DFe.NET → XML; junta todos os erros por campo)
  → ICertificadoAbridor (descriptografa o pfx do emitente, chave privada só em memória)
  → INfeAssinador (XmlDsigNfeAssinador: SignedXml, RSA-SHA1)
  → INfeXsdValidator (NfeXsdValidator: schemas 4.00 embutidos)
  → NfeAssinada (chave, dhEmi, XML)
```

`MontadorDeNfe` encadeia tudo isso e é reutilizado pela fase 2 antes da transmissão. O
`dhEmi` vai no fuso da UF do emitente (`Uf.NoFuso`), e o AAMM da chave sai da mesma data.
- Calcular imposto no emissor.
- Guardar segredo em claro ou devolvê-lo em resposta.

## Transmissão (módulo Nfe, fase 2)

```text
POST /emitentes/{id}/nfe ──► EmitirNfeHandler: valida (estrutura, emitente, certificado, série,
                              mapeamento com chave provisória), idempotência, grava Recebida
NfeWorker (BackgroundService) ──► INfeFila.RetirarAsync (UPDATE ... SKIP LOCKED) ──► escopo com AmbientTenant
  ──► ProcessarNfeHandler
        1. sem XML: ISerieRepository.ReservarNumeroAsync → MontadorDeNfe → grava número, chave e XML
        2. tentativa ≥ 2: ISefazGateway.ConsultarPorChaveAsync (100 → Autorizada; 217 → segue)
        3. ISefazGateway.AutorizarAsync (enviNFe indSinc=1) → protNFe → Autorizada | Denegada | Rejeitada
           sem protNFe: 103/105/108/109 → PendenteConsulta (backoff); rede/timeout → PendenteConsulta
ISefazGateway = SoapSefazGateway (HttpClient + certificado no TLS; MensagensSefaz; EnderecosSefaz)
```

O `procNFe` é concatenação textual do XML assinado com o `protNFe` que voltou, sem reserializar:
qualquer reserialização poderia alterar a assinatura.

## DANFE e webhook (módulo Nfe, fase 3)

- `IDanfeRenderer` → `QuestPdfDanfeRenderer`: `DanfeLeitor` (XML → `DanfeDados`) + `DanfeDocument`
  (QuestPDF) + `Code128` (código de barras). Só nota com protocolo tem DANFE pelo endpoint.
- Webhook: `AgendadorDeWebhook` grava a `WebhookEntrega` no mesmo `SaveChanges` do novo status da
  nota; `WebhookWorker` retira da fila (SKIP LOCKED) e `HttpWebhookEntregador` faz o POST assinado
  (`AssinaturaWebhook`). Workers de nota e de webhook compartilham `WorkerOptions`.

## Eventos (módulo Nfe, fase 4)

`TransmissorDeEventos` (monta → assina `infEvento` → valida XSD → `ISefazGateway.EnviarEventoAsync` →
grava retorno) é usado por `CancelarNfeHandler` e `EnviarCartaCorrecaoHandler`; `InutilizarHandler`
faz o mesmo caminho com `infInut`. Tabelas `nfe.eventos` e `nfe.inutilizacoes`. Nada passa pela
fila: são síncronos e retomáveis (ADR-021).

## Produção (fase 5)

- Contingência: estado do `Emitente` (`ContingenciaDesde`, `ContingenciaJustificativa`); `DestinoSefaz.Do(emitente)`
  carrega a decisão até o gateway, que troca os endereços pela SVC-AN (`EnderecosSefaz.UrlSvcAn`).
- Recibo: `ProcessarNfeHandler` consulta `nota.Recibo` antes da chave.
- `CertificadoVencimentoWorker` (diário) e `CertificadosHealthCheck` leem `IEmitenteRepository.ListarCertificadosVencendoAsync`,
  a única consulta que ignora o filtro de tenant (e por isso vive numa porta própria e devolve só o necessário).
- Rate limit no host (`Program.cs`), particionado pela claim `api_key_id`.
- `Emissor.Contracts` é empacotável (`dotnet pack`) e traz `EmissorClient`.