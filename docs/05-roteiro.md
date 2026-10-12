# Roteiro

Cada fase termina com testes passando e um checkpoint manual no Swagger.

- [x] **Fase 0: fundação.** Solution, módulos Tenancy e Emitentes, API key, certificado A1
  criptografado, séries, migrations, testes unitários e de integração (Testcontainers).
- [x] **Fase 1: núcleo NF-e offline.** DTO de emissão, mapeamento para DFe.NET (com IBS/CBS),
  XML, validação XSD, chave de acesso, assinatura, endpoint de prévia. Sem SEFAZ.
  Pendências conhecidas: CNPJ alfanumérico na chave de acesso (NT 2025.001) e `infRespTec`
  (exigido por algumas UFs) ficam para a fase 2.
- [x] **Fase 2: transmissão em homologação (SVRS).** Status do serviço, autorização síncrona,
  consulta por chave, worker com fila no Postgres, idempotência, numeração atômica,
  `PendenteConsulta`, reenvio corrigido, `?aguardar`, listagem e XML. Testada com SEFAZ falsa.
  Checkpoint real: status do serviço no SVRS de homologação respondeu 107 com o e-CNPJ real
  (12/10/2026); a primeira nota autorizada e a rejeição proposital ficam para o teste manual.
  Pendências para a fase 5: contingência SVC-AN, consulta de recibo (NFeRetAutorizacao4),
  `infRespTec`, CNPJ alfanumérico na chave.
- [x] **Fase 3: DANFE e entrega.** (12/10/2026)
  - [x] DANFE própria em QuestPDF (ADR-019): `GET /nfe/{id}/danfe` e prévia em PDF.
  - [x] Webhook por tenant com HMAC, fila e retentativas (ADR-020): `/api/v1/webhook`.
  Pendência: cache do PDF da DANFE (hoje é gerado a cada pedido, ~100 ms).
- [x] **Fase 4: eventos.** (12/10/2026) Cancelamento (24 h), carta de correção (sequência, até 20),
  inutilização (CNPJ), XML `procEventoNFe`/`procInutNFe`, eventos retomáveis quando a SEFAZ não
  responde, DANFE com marca de cancelada, webhook `nfe.cancelada`/`nfe.carta_correcao`, dígito da IE
  do destinatário (RJ). Pendências: cancelamento extemporâneo e algoritmos de IE de outras UFs.
- [x] **Fase 5: produção.** (12/10/2026) Contingência SVC-AN (manual, ADR-022), consulta de recibo
  (ADR-023), `infRespTec` e CNPJ alfanumérico (ADR-024), health e webhook de certificado a vencer,
  rate limit por chave, OpenAPI publicado, `EmissorClient` no pacote `Emissor.Contracts`, runbook
  (`06-operacao.md`) e compose para o Coolify (`deploy/coolify`). Pendências: cancelamento
  extemporâneo, IE de outras UFs, cache da DANFE, inutilização por CPF (limite do layout).
- [ ] **Fase 6 (no erp_flv):** módulo `Fiscal` consumindo o emissor.
