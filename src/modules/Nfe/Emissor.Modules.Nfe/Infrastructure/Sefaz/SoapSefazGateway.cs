using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Emissor.Modules.Nfe.Application.Ports;
using Emissor.Modules.Nfe.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Emissor.Modules.Nfe.Infrastructure.Sefaz;

public sealed class SefazOptions
{
    public const string Secao = "Nfe:Sefaz";

    /// <summary>Tempo maximo de cada chamada. A autorizacao sincrona do SVRS costuma responder em poucos segundos.</summary>
    public int TimeoutSegundos { get; set; } = 30;
}

/// <summary>
/// Fala SOAP 1.2 com os webservices da NF-e, com o certificado do emitente
/// na conexao TLS (e assim que a SEFAZ sabe quem chama). Um <c>HttpClient</c>
/// por certificado, reaproveitado entre chamadas, porque abrir TLS com
/// certificado de cliente e caro.
/// </summary>
public sealed class SoapSefazGateway(IOptions<SefazOptions> options, ILogger<SoapSefazGateway> logger) : ISefazGateway, IDisposable
{
    private readonly ClientesPorCertificado _clientes = new(TimeSpan.FromSeconds(options.Value.TimeoutSegundos));

    public async Task<StatusServicoSefaz> ConsultarStatusServicoAsync(DestinoSefaz destino, X509Certificate2 certificado, CancellationToken cancellationToken)
    {
        var mensagem = MensagensSefaz.ConsStatServ(Uf.Codigo(destino.Uf), destino.Ambiente);
        var resposta = await ChamarAsync(destino, ServicoSefaz.StatusServico, certificado, mensagem, cancellationToken);

        return MensagensSefaz.LerRetConsStatServ(resposta);
    }

    public async Task<RetornoAutorizacao> AutorizarAsync(DestinoSefaz destino, X509Certificate2 certificado, string xmlNfeAssinado, long idLote, CancellationToken cancellationToken)
    {
        var mensagem = MensagensSefaz.EnviNFe(idLote, xmlNfeAssinado);
        var resposta = await ChamarAsync(destino, ServicoSefaz.Autorizacao, certificado, mensagem, cancellationToken);

        return MensagensSefaz.LerRetEnviNFe(resposta);
    }

    public async Task<RetornoConsulta> ConsultarPorChaveAsync(DestinoSefaz destino, X509Certificate2 certificado, ChaveAcesso chave, CancellationToken cancellationToken)
    {
        var mensagem = MensagensSefaz.ConsSitNFe(destino.Ambiente, chave);
        var resposta = await ChamarAsync(destino, ServicoSefaz.ConsultaProtocolo, certificado, mensagem, cancellationToken);

        return MensagensSefaz.LerRetConsSitNFe(resposta);
    }

    public async Task<RetornoRecibo> ConsultarReciboAsync(DestinoSefaz destino, X509Certificate2 certificado, string recibo, CancellationToken cancellationToken)
    {
        var mensagem = MensagensSefaz.ConsReciNFe(destino.Ambiente, recibo);
        var resposta = await ChamarAsync(destino, ServicoSefaz.RetornoAutorizacao, certificado, mensagem, cancellationToken);

        return MensagensSefaz.LerRetConsReciNFe(resposta);
    }

    public async Task<RetornoEvento> EnviarEventoAsync(DestinoSefaz destino, X509Certificate2 certificado, string xmlEventoAssinado, long idLote, CancellationToken cancellationToken)
    {
        var mensagem = MensagensEventos.EnvEvento(idLote, xmlEventoAssinado);
        var resposta = await ChamarAsync(destino, ServicoSefaz.RecepcaoEvento, certificado, mensagem, cancellationToken);

        return MensagensEventos.LerRetEnvEvento(resposta);
    }

    public async Task<RetornoInutilizacao> InutilizarAsync(DestinoSefaz destino, X509Certificate2 certificado, string xmlInutAssinado, CancellationToken cancellationToken)
    {
        var resposta = await ChamarAsync(destino, ServicoSefaz.Inutilizacao, certificado, xmlInutAssinado, cancellationToken);

        return MensagensEventos.LerRetInutNFe(resposta);
    }

    private async Task<System.Xml.Linq.XElement> ChamarAsync(DestinoSefaz destino, ServicoSefaz servico, X509Certificate2 certificado, string mensagem, CancellationToken cancellationToken)
    {
        var url = EnderecosSefaz.Url(destino.Uf, destino.Ambiente, servico, destino.Contingencia);
        var cliente = _clientes.Obter(certificado);

        using var conteudo = new StringContent(MensagensSefaz.Envelope(servico, mensagem), Encoding.UTF8);
        // SOAP 1.2: a action vai como parametro do content-type. O header SOAPAction e redundante, mas inofensivo.
        conteudo.Headers.ContentType = new MediaTypeHeaderValue("application/soap+xml")
        {
            CharSet = "utf-8",
            Parameters = { new NameValueHeaderValue("action", "\"" + EnderecosSefaz.Action(servico) + "\"") }
        };

        using var requisicao = new HttpRequestMessage(HttpMethod.Post, url) { Content = conteudo };
        requisicao.Headers.TryAddWithoutValidation("SOAPAction", EnderecosSefaz.Action(servico));

        HttpResponseMessage resposta;

        try
        {
            resposta = await cliente.SendAsync(requisicao, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Falha de rede ao chamar {Servico} em {Url}.", servico, url);
            throw new SefazIndisponivelException($"Falha de rede ao chamar {servico}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Timeout ao chamar {Servico} em {Url}.", servico, url);
            throw new SefazIndisponivelException($"A SEFAZ não respondeu ao {servico} em {options.Value.TimeoutSegundos} s.", ex);
        }

        using (resposta)
        {
            var corpo = await resposta.Content.ReadAsStringAsync(cancellationToken);

            if (!resposta.IsSuccessStatusCode)
            {
                logger.LogWarning("HTTP {Status} ao chamar {Servico} em {Url}.", (int)resposta.StatusCode, servico, url);
                throw new SefazIndisponivelException($"A SEFAZ respondeu HTTP {(int)resposta.StatusCode} ao {servico}.");
            }

            return MensagensSefaz.ExtrairResultado(corpo);
        }
    }

    public void Dispose() => _clientes.Dispose();
}

/// <summary>
/// Um <c>HttpClient</c> por certificado (chave: thumbprint), com uma COPIA do
/// certificado que pertence ao cache. Quem chama o gateway descarta o
/// certificado ao fim da operacao; se o handler TLS guardasse aquela
/// instancia, o proximo handshake (apos a conexao expirar) iria sem
/// certificado valido e a SEFAZ responderia HTTP 403.
/// </summary>
public sealed class ClientesPorCertificado(TimeSpan timeout) : IDisposable
{
    private readonly ConcurrentDictionary<string, (X509Certificate2 Certificado, HttpClient Cliente)> _entradas = new(StringComparer.Ordinal);

    public HttpClient Obter(X509Certificate2 certificado) =>
        _entradas.GetOrAdd(certificado.Thumbprint, _ => Criar(certificado)).Cliente;

    /// <summary>Para os testes: o certificado que o cache guarda continua utilizavel apos o original ser descartado.</summary>
    public X509Certificate2? CertificadoGuardado(string thumbprint) =>
        _entradas.TryGetValue(thumbprint, out var entrada) ? entrada.Certificado : null;

    private (X509Certificate2, HttpClient) Criar(X509Certificate2 original)
    {
        // Copia com a chave privada: sobrevive ao Dispose do original.
        var copia = new X509Certificate2(original);

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            SslOptions =
            {
                ClientCertificates = [copia],
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                // O certificado do servidor da SEFAZ e da ICP-Brasil, que o sistema nao conhece.
                RemoteCertificateValidationCallback = CadeiaIcpBrasil.ValidarServidor
            }
        };

        return (copia, new HttpClient(handler, disposeHandler: true) { Timeout = timeout });
    }

    public void Dispose()
    {
        foreach (var (certificado, cliente) in _entradas.Values)
        {
            cliente.Dispose();
            certificado.Dispose();
        }

        _entradas.Clear();
    }
}
