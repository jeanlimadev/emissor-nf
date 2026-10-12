# Deploy no Coolify

Um deploy por ambiente, cada um com seu Postgres e suas variáveis:

| Ambiente | Nome no Coolify | Emitentes com `ambiente` | Domínio (exemplo) |
|---|---|---|---|
| Homologação | `emissor-homolog` | 2 | `emissor-homolog.seudominio.com.br` |
| Produção | `emissor-prod` | 1 | `emissor.seudominio.com.br` |

1. No Coolify, crie um recurso "Docker Compose" apontando para este repositório e para
   `deploy/coolify/docker-compose.yml`.
2. Defina os segredos: `POSTGRES_PASSWORD`, `ADMIN_TOKEN`, `CHAVE_MESTRA` (`openssl rand -base64 32`
   cada um) e, se quiser o `infRespTec`, `RESPTEC_*`. Gere valores diferentes para cada ambiente.
3. Exponha o serviço `api` na porta 8080 com TLS pelo proxy do Coolify. A API só deve ser alcançada por
   HTTPS: a chave de API vai no header.
4. Ative o backup diário do volume `pgdata` (ou `pg_dump` agendado). O banco é o arquivo fiscal.
5. Primeira subida e rotina: `docs/06-operacao.md`.

A imagem já traz as raízes da ICP-Brasil embutidas no módulo Nfe e a fonte da DANFE; não há
dependência de sistema além do que o Dockerfile instala.
