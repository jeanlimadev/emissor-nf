# Domínio da NF-e: vocabulário

Termos usados no código, nos nomes de classe e nas mensagens. Quando o layout da SEFAZ e o
português do dia a dia divergem, o nome aqui é o que vale.

| Termo | O que é | No layout |
|---|---|---|
| **Tenant** | Um sistema cliente do emissor (o ERP de um cliente, outro projeto). Dono de API keys, emitentes e notas. | — |
| **API key** | Chave `emk_<prefixo>_<segredo>` que identifica o tenant. Só o hash é guardado. | — |
| **Emitente** | Quem assina e emite: CPF de produtor rural com IE, ou CNPJ. Pertence a um tenant. | grupo `emit` |
| **Inscrição Estadual (IE)** | Cadastro do emitente na SEFAZ da UF. Obrigatória para emitir NF-e, inclusive para a pessoa física. | `emit/IE` |
| **CRT** | Código de Regime Tributário: 1 Simples, 2 Simples com excesso de sublimite, 3 Regime normal, 4 MEI. | `emit/CRT` |
| **Ambiente** | 1 produção, 2 homologação. Em homologação a nota não tem valor fiscal e o nome do destinatário é um texto fixo exigido pela SEFAZ. | `ide/tpAmb` |
| **Certificado A1** | Arquivo `.pfx` com chave privada e senha, emitido pela ICP-Brasil (e-CPF ou e-CNPJ). Assina o XML. Vale 1 ano. | — |
| **Série** | Agrupador de numeração do emitente (1 a 999). Cada série tem sua sequência de números. | `ide/serie` |
| **Número** | Sequencial da nota dentro da série (1 a 999.999.999). Não pode ter furo sem inutilização. | `ide/nNF` |
| **Chave de acesso** | 44 dígitos que identificam a nota: UF, AAMM, documento do emitente, modelo, série, número, tipo de emissão, código numérico e DV. | `infNFe/@Id` |
| **Autorizador** | O webservice da SEFAZ que recebe a nota. RJ usa o SVRS (Sefaz Virtual do RS). | — |
| **cStat / xMotivo** | Código e texto do retorno da SEFAZ. `100` autorizada; `1xx` outros sucessos; `2xx`–`9xx` rejeições. | `protNFe/infProt` |
| **Protocolo** | Número que a SEFAZ devolve ao autorizar. Prova de que a nota existe. | `infProt/nProt` |
| **procNFe** | O XML final: a nota assinada mais o protocolo. É o que se guarda e distribui. | `nfeProc` |
| **DANFE** | Representação impressa (PDF) da NF-e. Acompanha a mercadoria. | — |
| **Evento** | Mudança sobre nota autorizada: cancelamento (110111), carta de correção (110110). | `evento` |
| **Carta de correção (CC-e)** | Evento 110110: corrige erro que não muda imposto, partes nem datas. Sequencial por nota; a última substitui as anteriores. | `evento` |
| **Cancelamento** | Evento 110111: desfaz uma nota autorizada, em até 24 h, com justificativa. A nota continua existindo, cancelada. | `evento` |
| **Inutilização** | Declarar à SEFAZ que uma faixa de números não será usada (furo na numeração). | `inutNFe` |
| **Denegada** | A SEFAZ recusou por irregularidade cadastral do emitente ou destinatário. O número é consumido. | `cStat` 110/301/302 |
| **Contingência** | Emitir quando o autorizador está fora. Para o SVRS, usa-se a SVC-AN com `tpEmis` 6, informando quando entrou (`dhCont`) e por quê (`xJust`). Ligada e desligada por emitente. | `ide/tpEmis` |
| **Recibo (nRec)** | Comprovante de que a SEFAZ recebeu o lote (cStat 103) sem processar na hora. Consulta-se pelo recibo até o lote ser processado (104). | `infRec/nRec` |
| **Responsável técnico** | Empresa que desenvolveu o sistema emissor, informada em toda nota (NT 2018.005). Configuração do serviço. | `infRespTec` |
| **Código numérico (cNF)** | 8 dígitos aleatórios dentro da chave, para a chave não ser previsível. Não pode ser igual ao número da nota. | `ide/cNF` |
| **DV** | Dígito verificador da chave: módulo 11, pesos 2 a 9 da direita para a esquerda. | `ide/cDV` |
| **Schema (XSD)** | Os arquivos oficiais do layout 4.00 que definem cada campo. O emissor valida o XML contra eles antes de transmitir; estão embutidos no assembly do módulo Nfe. | `nfe_v4.00.xsd` |
| **Assinatura** | XMLDSig sobre o `infNFe`: C14N, digest SHA-1, RSA-SHA1, certificado no `KeyInfo`. É a prova de que o emitente autorizou aquele conteúdo. | `NFe/Signature` |
| **Prévia** | A NF-e montada, assinada e validada no schema, mas não transmitida nem numerada de fato. Checkpoint para conferir o XML. | — |
| **Idempotency-Key** | Chave que o sistema cliente manda no header da emissão. Repetir o mesmo pedido com a mesma chave devolve a mesma nota, nunca uma segunda. | — |
| **Lote / recibo** | A SEFAZ recebe notas em lote (aqui, lote de 1, síncrono). Quando não processa na hora, devolve um recibo (`nRec`, cStat 103) para consulta posterior. | `enviNFe`, `infRec` |
| **Pendente de consulta** | Nota transmitida sem resposta conclusiva. Antes de reenviar, consulta-se pela chave: a SEFAZ pode ter autorizado. | `consSitNFe` |
| **Webhook** | URL do sistema cliente que recebe um POST assinado (HMAC-SHA256) a cada status final de nota. Entregas ficam registradas e são retentadas. | — |
| **IBS / CBS** | Tributos da Reforma Tributária, obrigatórios na NF-e desde 03/08/2026 (NT 2025.002). Vêm calculados do sistema cliente. | grupo `IBSCBS` |

## Pessoa física emitindo

Desde o Ajuste SINIEF 09/2017 (NT 2018.001) a NF-e aceita CPF no grupo `emit`. Na prática isso
é o produtor rural com IE vinculada ao CPF. Ele precisa de e-CPF A1 e de credenciamento na
SEFAZ da UF, em homologação e em produção. Para o serviço, PF e PJ são o mesmo fluxo: muda o
identificador e o certificado.
