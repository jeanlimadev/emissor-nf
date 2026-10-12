# CLAUDE.md — orientações para agentes

Este repositório é o **emissor-nf**: um microserviço de emissão de NF-e (modelo 55) que atende
vários sistemas clientes (o ERP `erp_flv` e outros projetos) e vários emitentes (produtor rural
pessoa física com Inscrição Estadual, identificado por CPF, e pessoa jurídica por CNPJ).

## Contexto e documentação

- `docs/01-dominio-nfe.md` — o vocabulário da NF-e usado no código (use estes termos).
- `docs/02-arquitetura.md` — módulos, camadas, tenancy, certificados, o que evitar.
- `docs/03-api.md` — contrato da API para os sistemas clientes.
- `docs/04-decisoes.md` — decisões (ADRs). Toda decisão nova entra aqui.
- `docs/05-roteiro.md` — as fases do projeto e o que falta em cada uma.
- `docs/06-operacao.md` — runbook: variáveis, deploy, rotina, problemas comuns.

## Arquitetura (resumo)

- .NET 10, Minimal APIs, EF Core + PostgreSQL (um banco, um schema por módulo), Result Pattern,
  `ProblemDetails` com código estável na extensão `code`, CQRS leve sem MediatR.
- Uma class library por módulo com `Domain/Application/Infrastructure/Presentation` e um arquivo
  `XxxModule.cs` que registra serviços e rotas. Módulos: `Tenancy` (tenants e API keys),
  `Emitentes` (cadastro, certificado A1, séries), `Nfe` (emissão, eventos, DANFE).
- **Multi-tenant**: o tenant vem da API key autenticada (`ICurrentTenant`), nunca do payload.
  Toda entidade de negócio tem `TenantId` e o `DbContext` do módulo aplica filtro global.
- **Impostos chegam prontos** do sistema cliente. O emissor valida contra o schema e transmite;
  ele não é motor fiscal.
- A biblioteca de NF-e (DFe.NET) fica só em `Infrastructure`, atrás de portas (`ISefazGateway`,
  `INfeXmlBuilder`). Domínio e Application não a conhecem.

## Regras de trabalho

- Todo módulo e caso de uso novo vem com testes (unitários no projeto do módulo; integração em
  `tests/Emissor.Api.Tests`, que sobe Postgres via Testcontainers).
- Segredos nunca em log nem em resposta: senha de certificado, bytes do pfx, chave de API em
  texto, connection string. Use `SecretRedactor` quando o texto vem de fora.
- Erros esperados são `Result`/`Error`; exceção é só para o inesperado.
- Comentários e mensagens em português; códigos de erro em `modulo.motivo` (ex.: `emitente.cpf_invalido`).
- Migrations: `dotnet ef migrations add Nome -o Infrastructure/Persistence/Migrations` dentro do
  projeto do módulo (ferramenta local em `.config/dotnet-tools.json`).
- Schemas XSD do layout 4.00 ficam embutidos em `Nfe/Infrastructure/Xml/Schemas`. Para atualizar,
  baixe o pacote de liberação (PL) novo do Portal da NF-e e substitua os arquivos.
- Transmissão: o worker (`NfeWorker`) roda dentro do host e tira notas da tabela `nfe.notas`
  (`FOR UPDATE SKIP LOCKED`). Configuração em `Nfe:Worker` (intervalo, espera entre tentativas, posse)
  e `Nfe:Sefaz` (timeout). Endereços dos webservices em `Nfe/Infrastructure/Sefaz/EnderecosSefaz.cs`.
  Toda segunda tentativa consulta a SEFAZ pela chave antes de reenviar.
- DANFE: leiaute em `Nfe/Infrastructure/Danfe/DanfeDocument.cs` (QuestPDF), dados lidos do XML por
  `DanfeLeitor`. Mudou o leiaute oficial? Edita-se o documento, e os testes em `DanfeTests` gravam
  PDFs de amostra quando a variável `DANFE_SAIDA` aponta para uma pasta.
- Contingência SVC-AN é estado do emitente e decisão manual (ADR-022); o gateway troca os endereços
  por `DestinoSefaz.Contingencia`. Recibo (103) é consultado antes da chave (ADR-023).
- Eventos (cancelamento, CC-e, inutilização): síncronos e retomáveis; mensagens em `MensagensEventos`,
  regras em `EventoNfe`/`Inutilizacao`. Prazo de cancelamento em `EventoNfe.PrazoCancelamento`.
- Webhook: `AgendadorDeWebhook` enfileira na mesma transação do status; `WebhookWorker` entrega com
  `X-Emissor-Signature` (HMAC-SHA256). Evento novo = adicionar em `EventosWebhook`.
- O TLS da SEFAZ é da ICP-Brasil: as raízes ficam embutidas em `Nfe/Infrastructure/Sefaz/CadeiaIcpBrasil`
  (ADR-018). Raiz nova = adicionar o `.crt` baixado do ITI nessa pasta.
- A assinatura da NF-e é RSA-SHA1 (exigência da SEFAZ). Em Fedora/RHEL o OpenSSL bloqueia SHA-1;
  `OpenSslSha1.PermitirSeNecessario()` resolve no processo e já é chamado no `Program.cs` e nos
  `ModuleInitializer` dos testes. Qualquer novo executável ou projeto de teste que assine XML
  precisa chamá-lo antes do primeiro uso de criptografia.

## Rodando local

```bash
docker compose up -d                       # Postgres em localhost:5435
dotnet run --project src/host/Emissor.Api  # Swagger em http://localhost:5176/swagger
dotnet test src/Emissor.slnx               # unitários + integração (precisa de Docker)
```

Token de administração em desenvolvimento: `dev-admin-token` (header `X-Admin-Token`).

Depuração no VS Code (C# Dev Kit): `.vscode/launch.json` tem os perfis "API (debug)" e
"API (debug + abrir Swagger)"; F5 compila e sobe a API com o depurador anexado, e breakpoints
valem em qualquer projeto da solution. As tarefas em `.vscode/tasks.json` cobrem build, testes e
o Postgres do compose. A variável `DOTNET_USE_POLLING_FILE_WATCHER=1` já está nesses perfis.
