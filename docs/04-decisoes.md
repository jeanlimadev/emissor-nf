# Decisões (ADRs)

Formato: número, data, decisão, motivo. Decisão revista ganha uma entrada nova que cita a antiga.

## ADR-001 (2026-10-11): serviço separado do ERP, multi-tenant

Um único serviço atende vários sistemas clientes e vários emitentes, com API key por tenant.
Motivo: outros projetos vão emitir nota; replicar a emissão em cada um é retrabalho e risco.

## ADR-002 (2026-10-11): só NF-e modelo 55 no MVP

NFC-e, NFS-e, CT-e e MDF-e ficam fora. Motivo: o caso central é a venda da distribuidora e do
produtor rural; NFS-e é outro protocolo inteiro.

## ADR-003 (2026-10-11): pessoa física = produtor rural com IE

"Emitir como pessoa física" significa CPF no grupo `emit`, com IE e e-CPF. Mesmo fluxo da PJ.

## ADR-004 (2026-10-11): integração direta com a SEFAZ, biblioteca DFe.NET

Sem API de terceiros. Biblioteca DFe.NET (ZeusAutomacao, LGPL-2.1) para XML, assinatura e SOAP,
DANFE via QuestPDF. Unimake.DFe (MIT) é o plano B. A biblioteca fica atrás de portas em
`Infrastructure`. Motivo: aprendizado do protocolo, sem custo por nota, biblioteca madura e com
a Reforma Tributária (NT 2025.002) implementada.

## ADR-005 (2026-10-11): .NET 10 e as convenções do erp_flv

Mesma stack e mesmo estilo de módulo do ERP. Motivo: o autor domina, e o cliente .NET do
emissor nasce natural.

## ADR-006 (2026-10-11): impostos chegam calculados

O sistema cliente envia CST/CSOSN, bases, alíquotas, valores e os grupos IBS/CBS. O emissor
valida contra o schema e transmite. Motivo: calcular imposto é motor fiscal, outro produto.

## ADR-007 (2026-10-11): certificado guardado com AES-GCM e chave mestra do ambiente

Pfx e senha criptografados no banco com chave mestra vinda de variável de ambiente. Metadados
em claro. Motivo: o serviço precisa assinar sem intervenção humana, mas o banco vazado não
pode entregar certificados. Chave fora do banco é o mínimo; cofre externo fica para depois.

## ADR-008 (2026-10-11): token único de administração

Tenants e chaves são criados por um token de operação (`Admin:Token`), não por um tenant.
Motivo: no MVP quem opera o serviço é uma pessoa; usuário/senha e painel ficam para quando
houver mais gente.

## ADR-009 (2026-10-11): DFe.NET só para serializar; chave, assinatura e XSD são nossos

A DFe.NET entra como modelo de classes do layout 4.00 (com IBS/CBS) e serializador XML. A chave
de acesso (`ChaveAcesso`), a assinatura (`XmlDsigNfeAssinador`, `SignedXml` do .NET) e a
validação XSD (`NfeXsdValidator`, schemas embutidos no assembly) são implementação própria.
Motivo: são as três peças que mais ensinam o protocolo, cabem em poucas dezenas de linhas cada e
ficam testáveis sem a `ConfiguracaoServico` global da biblioteca. A transmissão SOAP (fase 2)
continua com a DFe.NET.

## ADR-010 (2026-10-11): totais somados pelo emissor, impostos por item vêm do cliente

O cliente envia os impostos de cada item já calculados (ADR-006). `ICMSTot` e `IBSCBSTot` são
somas desses itens, e `vNF` segue a fórmula do layout; o emissor faz essa conta e confere que o
pagamento fecha. Motivo: somar não é motor fiscal, e deixar o cliente enviar totais só abriria
espaço para divergência que a SEFAZ rejeita (cStat 610, 865).

## ADR-011 (2026-10-11): erros de validação com detalhes por campo

`Error` ganhou `Details` (campo -> mensagens), que vai na extensão `errors` do ProblemDetails. O
mapeamento para XML não para no primeiro problema: devolve todos. Motivo: um pedido de emissão
tem centenas de campos; corrigir um por vez seria insuportável para o sistema cliente.

## ADR-012 (2026-10-11): assinatura RSA-SHA1 e o shim para Fedora/RHEL

A SEFAZ exige RSA-SHA1 e digest SHA-1 na assinatura da NF-e; não há opção. Fedora e RHEL
bloqueiam SHA-1 em assinaturas pela política de criptografia DEFAULT, e o .NET falha com
"invalid digest". `OpenSslSha1.PermitirSeNecessario()` detecta essa política e aponta
`OPENSSL_CONF` para uma configuração que libera SHA-1 só no processo, antes do primeiro uso de
criptografia (início do `Program.cs` e `ModuleInitializer` dos testes). Em Debian (imagem do
container), macOS e Windows é um no-op. Alternativa descartada: implementar RSA PKCS#1 à mão com
`BigInteger`, que funcionaria em qualquer política mas colocaria criptografia caseira num
sistema fiscal.

## ADR-013 (2026-10-11): prévia da NF-e como checkpoint da fase 1

`POST /emitentes/{id}/nfe/previa` monta, assina e valida sem transmitir nem consumir número.
Motivo: dá ao sistema cliente (e ao autor) um jeito de ver o XML exato que sairia, e permite
fechar a fase 1 com um checkpoint manual no Swagger antes de existir fila e SEFAZ.

## ADR-014 (2026-10-12): cliente SOAP próprio; DFe.NET só para as classes do layout

A conversa com a SEFAZ (`SoapSefazGateway`) é nossa: SOAP 1.2 com `HttpClient`, certificado do emitente
no TLS, mensagens montadas e lidas em `MensagensSefaz`, endereços em `EnderecosSefaz`. Isso revisa a
parte "SOAP via DFe.NET" da ADR-004. Motivo: a DFe.NET transmite objetos tipados que ela mesma
serializa, e reserializar o XML assinado arriscaria a assinatura; além disso a configuração dela é um
singleton estático, ruim para muitos certificados ao mesmo tempo. As três mensagens são pequenas, e os
endereços ficam numa tabela só, que é o que muda quando a SEFAZ muda. A DFe.NET continua como modelo
de classes do layout 4.00 e plano B para a transmissão.

## ADR-015 (2026-10-12): a fila é a própria tabela de notas, no Postgres

Sem broker. Nota com status `Recebida`/`PendenteConsulta` e `proxima_tentativa_em` vencido é um
trabalho; o worker a retira com `UPDATE ... FOR UPDATE SKIP LOCKED` e a marca `EmProcessamento`.
Várias instâncias podem rodar o worker. Posse de 5 min: nota parada em processamento por mais tempo
é retomada. Motivo: um banco só para operar, transação com o estado da nota, e volume que um
`SELECT ... SKIP LOCKED` resolve com folga.

## ADR-016 (2026-10-12): número consumido no processamento, por UPDATE atômico

O número da série é reservado pelo worker, imediatamente antes de montar o XML, com
`UPDATE emitentes.series SET proximo_numero = proximo_numero + 1 ... RETURNING`. Fica consumido mesmo
que a nota falhe depois; rejeição mantém o número para o reenvio corrigido; furo real é inutilização
(fase 4). Motivo: o plano previa lock na mesma transação da nota, mas série e nota vivem em módulos
(schemas) diferentes com DbContexts próprios; o UPDATE atômico dá a mesma garantia de unicidade sem
transação distribuída, e o "número perdido" já tem o remédio fiscal correto.

## ADR-017 (2026-10-12): consultar pela chave antes de reenviar

Toda segunda tentativa começa com `NfeConsultaProtocolo4`. Motivo: timeout não significa que a nota não
entrou; reenviar às cegas gera rejeição 204/539 ou, pior, confusão entre o que o cliente acha que
aconteceu e o que a SEFAZ registrou. A consulta é barata e resolve os dois casos (consta: autorizada;
não consta: transmite).

## ADR-018 (2026-10-12): raízes da ICP-Brasil embutidas para confiar no TLS da SEFAZ

Os webservices da SEFAZ (o SVRS apresenta um certificado da AC do SERPRO, raiz "AC Raiz Brasileira
v10") usam a cadeia da ICP-Brasil, que não está na lista de confiança do Linux nem da imagem do
container: a conexão cai com `PartialChain`. As raízes v5, v10, v11, v12 e v13, baixadas do ITI
(`acraiz.icpbrasil.gov.br/credenciadas/RAIZ/`), ficam embutidas no módulo Nfe
(`CadeiaIcpBrasil`). A validação padrão do sistema vem primeiro; só o erro de cadeia é reavaliado
contra essas raízes, mantendo nome do host, validade e revogação. Alternativa descartada:
desligar a validação do certificado do servidor (como a opção `ValidarCertificadoDoServidor=false`
da DFe.NET), que deixaria a emissão exposta a um intermediário. Ao surgir uma raiz nova (v14...),
basta adicionar o `.crt` na pasta.

## ADR-019 (2026-10-12): DANFE própria em QuestPDF, lida do XML

A DANFE é desenhada por nós (`DanfeDocument`) com o QuestPDF (licença Community, válida para
receita anual abaixo de US$ 1 milhão; fonte Lato embutida, nada a instalar no container), seguindo o
leiaute do Anexo II do Manual de Orientação do Contribuinte, com código de barras CODE-128C também
nosso (`Code128`). A entrada é o XML (`nfeProc` ou `NFe`), lido por `DanfeLeitor`, e não o pedido:
a DANFE mostra o que foi assinado e autorizado. Motivo: o pacote `Zeus.Net.NFe.Danfe.QuestPdf` da
DFe.NET só traz DANFE de NFC-e, eventos e relatórios; as alternativas (PdfClown, HTML) trazem
dependências pesadas ou renderização por navegador. Com o leiaute em código legível, uma mudança de
NT (como os totais de IBS/CBS, já incluídos) é uma edição localizada. Só nota autorizada tem DANFE
pelo endpoint; denegada e cancelada saem com a marca correspondente; a prévia sai com "PRÉVIA -
SEM VALOR FISCAL"; homologação sempre com "SEM VALOR FISCAL".

## ADR-020 (2026-10-12): webhook assinado com HMAC, fila própria e entrega pelo worker

Um webhook por tenant (URL + segredo), com o segredo guardado no mesmo cofre dos certificados
(AES-GCM com a chave mestra). A entrega é gravada na tabela `nfe.webhook_entregas` na mesma
transação em que a nota muda de status; um `WebhookWorker` separado faz o POST com
`X-Emissor-Signature` (HMAC-SHA256 do corpo) e retenta com a mesma política de espera das notas.
Motivo: o sistema cliente precisa saber do resultado sem polling, com prova de origem (HMAC) e
sem perder evento quando estiver fora do ar (fila + retentativa). O corpo carrega a nota como
estava no momento do evento, não o estado atual, para o cliente ver a sequência real.

## ADR-021 (2026-10-12): eventos síncronos, retomáveis, com XML próprio

Cancelamento, carta de correção e inutilização chamam a SEFAZ na própria requisição, sem fila:
o RecepcaoEvento4 e o NfeInutilizacao4 respondem em segundos e o cliente precisa do resultado na
hora. O evento é gravado como `Pendente` com o XML assinado antes de transmitir; sem resposta
conclusiva, a mesma chamada repetida reaproveita o registro (mesma sequência, mesmo XML), o que
evita o evento duplicado (cStat 573). Mensagens em `MensagensEventos`, schemas oficiais
(envEventoCancNFe, envCCe, inutNFe) embutidos e validados antes do envio, assinatura pelo mesmo
`XmlDsigNfeAssinador` (agora genérico por elemento com `Id`). Prazo de cancelamento fixo em 24 h
(regra geral e do RJ); o cancelamento extemporâneo fica fora. Inutilização só para CNPJ porque o
`infInut` 4.00 não tem CPF. A IE do destinatário passou a ter o dígito conferido (RJ) antes do XML,
depois de a SEFAZ rejeitar um teste com cStat 210.

## ADR-022 (2026-10-12): contingência SVC-AN ligada por decisão de quem opera

A contingência é um estado do emitente (`POST/DELETE /emitentes/{id}/contingencia`), não uma
decisão automática do worker. Ligada, as notas saem com `tpEmis 6`, `dhCont` e `xJust`, e todas as
chamadas (autorização, recibo, consulta, eventos) vão para a SVC-AN; inutilização fica bloqueada
porque a SVC não a oferece. Motivo: a SVC só aceita quando a SEFAZ autorizadora a ativou para a UF, e
um erro de rede local não é motivo para mudar de autorizador; quem opera olha o aviso do SVRS e
decide. O retorno ao normal também é manual, para não alternar no meio de uma instabilidade.

## ADR-023 (2026-10-12): recibo antes da chave

Quando a SEFAZ só recebe o lote (cStat 103 com `nRec`), o worker consulta o recibo
(`NFeRetAutorizacao4`) antes de qualquer outra coisa: enquanto o lote processa (105), a consulta pela
chave diria "não consta" e o reenvio geraria duplicidade. Recibo inválido ou expirado é descartado e
a nota segue para a consulta pela chave. Motivo: é a sequência que o Manual de Orientação descreve
para o envio assíncrono, e o SVRS pode cair nela mesmo com `indSinc=1` em horário de pico.

## ADR-024 (2026-10-12): CNPJ alfanumérico na chave e `infRespTec` por configuração

A chave aceita letras maiúsculas nas 12 primeiras posições do CNPJ (formato do `Id` do `infNFe` no
schema: `NFe[0-9]{6}[0-9A-Z]{12}[0-9]{26}`), e o dígito verificador trata letra como ASCII-48, como a
NT 2025.001 define. O `infRespTec` (responsável técnico pelo software) vem da configuração
`Nfe:ResponsavelTecnico` e entra em todas as notas quando preenchido. Motivo: ambos são exigências que
chegam sem aviso por UF/emitente e precisam estar prontas antes da primeira nota de produção.

## ADR-025 (2026-10-12): produção com limite por chave, health de certificados e cliente tipado

Rate limit por chave de API (janela fixa de 1 minuto, 429 com código estável), health check que
degrada 30 dias antes de um certificado vencer e webhook diário `emitente.certificado_vencendo`,
OpenAPI publicado em todo ambiente, e `EmissorClient` no pacote `Emissor.Contracts` (só BCL). Deploy
por Docker Compose no Coolify, um por ambiente, com o runbook em `docs/06-operacao.md`. Motivo: são
as peças que separam "funciona em homologação" de "dá para deixar rodando sem olhar".
