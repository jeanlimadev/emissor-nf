# Operação (runbook)

Como subir, configurar e cuidar do emissor em homologação e produção. Dois deploys separados no
Coolify, cada um com seu banco: `emissor-homolog` e `emissor-prod`. O ambiente SEFAZ (homologação ou
produção) é decidido **por emitente** (`ambiente` no cadastro), mas misturar os dois no mesmo deploy
só confunde; mantenha um deploy por ambiente.

## Variáveis de ambiente

| Variável | O que é | Exemplo |
|---|---|---|
| `ConnectionStrings__Emissor` | Postgres do deploy | `Host=db;Database=emissor;Username=emissor;Password=...` |
| `Admin__Token` | Token do header `X-Admin-Token` (criar tenants e chaves). Longo e aleatório. | `openssl rand -base64 32` |
| `Certificados__ChaveMestra` | 32 bytes em base64. Cifra os certificados A1 e os segredos de webhook. **Perdeu, perdeu os certificados.** Guarde no cofre do Coolify e em um lugar fora dele. | `openssl rand -base64 32` |
| `Database__MigrateOnStartup` | `true` no container (padrão do Dockerfile): aplica as migrations na subida. | `true` |
| `Nfe__Worker__Ativo` | `false` em instâncias que só servem a API. Pelo menos uma instância precisa ter `true`. | `true` |
| `Nfe__Worker__IntervaloSegundos` | Pausa do worker com a fila vazia. | `2` |
| `Nfe__Worker__PosseSegundos` | Tempo para retomar nota presa em processamento. Maior que o timeout da SEFAZ. | `300` |
| `Nfe__Sefaz__TimeoutSegundos` | Timeout de cada chamada à SEFAZ. | `30` |
| `Nfe__ResponsavelTecnico__Cnpj` / `__Contato` / `__Email` / `__Telefone` | Grupo `infRespTec` (quem desenvolveu o sistema). Os quatro preenchidos ou nenhum. | |
| `RateLimit__PorMinuto` | Requisições por minuto por chave de API. | `300` |
| `Logging__Console__FormatterName` | `json` em produção, para o coletor de logs. | `json` |

Segredos nunca em `appsettings.json` do repositório: só por variável de ambiente ou cofre do Coolify.

## Primeira subida

1. Crie o Postgres (17) e o deploy da API apontando o Dockerfile `src/host/Emissor.Api/Dockerfile`
   com contexto na raiz do repositório. Porta interna 8080. Health check: `GET /health`.
2. Defina as variáveis acima. Gere `Admin__Token` e `Certificados__ChaveMestra` novos por deploy.
3. Subiu? `GET /health` responde `Healthy`. `GET /swagger/v1/swagger.json` é o contrato publicado
   (a UI do Swagger só existe em desenvolvimento).
4. Com `X-Admin-Token`: `POST /api/v1/admin/tenants` e `POST /api/v1/admin/tenants/{id}/api-keys`.
   A chave em texto aparece uma vez; entregue ao sistema cliente por canal seguro.
5. O sistema cliente, com `X-Api-Key`: cadastra o emitente, envia o `.pfx`, cria a série e configura o
   webhook. `GET /api/v1/emitentes/{id}/sefaz/status` com `cStat 107` fecha a instalação.

## Rotina

- **Health** (`/health`): `Degraded` quando algum certificado vence em até 30 dias; `Unhealthy` quando
  algum já venceu ou o banco caiu. Alarme nos dois casos.
- **Certificado a vencer**: o webhook `emitente.certificado_vencendo` avisa o sistema cliente uma vez por
  dia a partir de 30 dias antes. O cliente envia o novo `.pfx` em `PUT /emitentes/{id}/certificado`;
  nada mais muda.
- **Logs**: na subida aparecem dois avisos do `DataProtection` ("Storing keys in a directory", "No XML
  encryptor configured"). São esperados: o emissor não usa proteção de dados do ASP.NET para nada
  (os segredos usam a chave mestra própria). Cada nota processada aparece com `NfeId` e `TenantId` no escopo. Senha de certificado,
  pfx, chave de API e segredo de webhook nunca aparecem.
- **Rotação de chave de API**: crie a chave nova (`POST .../api-keys`), troque no sistema cliente,
  revogue a antiga (`DELETE .../api-keys/{id}`). As duas valem durante a troca.

## Quando algo dá errado

| Sintoma | O que olhar | O que fazer |
|---|---|---|
| Nota parada em `PendenteConsulta` com `xMotivo` de rede/timeout | `GET /nfe/{id}`: `tentativas` e `proximaTentativaEm` | Normal durante instabilidade da SEFAZ: o worker consulta pela chave e retransmite. Para antecipar: `POST /nfe/{id}/reenviar` sem corpo. |
| Nota em `Falha` com "Desistiu após 12 tentativas" | `GET /emitentes/{id}/sefaz/status` | SEFAZ fora por horas. Volte a nota com `POST /nfe/{id}/reenviar` (sem corpo) quando o status responder 107. |
| `cStat 108/109` no status, ou falha de rede persistente | Portal da NF-e: avisos do SVRS | Ligue a contingência: `POST /emitentes/{id}/contingencia` com justificativa. As notas passam a sair com `tpEmis 6` pela SVC-AN. Desligue com `DELETE` quando o SVRS voltar. Inutilização não funciona em contingência. |
| Nota em `EmProcessamento` por mais de 5 min | Logs do worker | O processo caiu no meio; outra instância retoma sozinha após `PosseSegundos`. |
| `HTTP 403` da SEFAZ | Certificado do emitente | Certificado não é ICP-Brasil, está vencido ou a IE não está credenciada. O status 107 funciona só com certificado válido. |
| `nfe.sefaz_indisponivel` em evento (cancelamento/carta) | `GET /nfe/{id}/eventos`: evento `Pendente` | Repita a mesma chamada: o emissor retoma o evento pendente sem duplicar. |
| Webhook `Falha` em `/webhook/entregas` | `ultimoHttpStatus`, `ultimoErro` | Corrija a URL ou o receptor e use `POST /webhook/testar`. Entregas com falha não são reenviadas automaticamente: o cliente consulta `GET /nfe/{id}` para recuperar o estado. |
| Furo na numeração (nota que falhou antes de ir) | `GET /nfe?status=Falha` e a série | `POST /emitentes/{id}/inutilizacoes` com a faixa. |
| Precisa de uma nota real cancelada | Prazo: 24 h da autorização | `POST /nfe/{id}/cancelamento`. Depois disso, só por processo na SEFAZ. |

## Backup e retenção

Os XMLs (`procNFe`, eventos, inutilizações) ficam no Postgres, no schema `nfe`. Backup diário do
banco é o backup fiscal: guarde por ao menos 5 anos. A chave mestra faz parte do backup: sem ela os
certificados do banco não abrem (os XMLs continuam legíveis).

## Entrando em produção com um emitente

1. Certificado A1 válido e IE credenciada em **produção** na SEFAZ da UF.
2. `PUT /emitentes/{id}` com `ambiente: 1` (ou um emitente novo no deploy de produção).
3. Série nova (a numeração de homologação não se mistura com a de produção).
4. Primeira nota com `?aguardar=30`, conferida no portal da NF-e pela chave.
5. Webhook configurado e testado antes da primeira nota.
