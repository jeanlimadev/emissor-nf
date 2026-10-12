using System.Reflection;
using System.Xml;
using System.Xml.Schema;
using Emissor.BuildingBlocks;
using Emissor.Modules.Nfe.Application.Ports;
using Emissor.Modules.Nfe.Domain;

namespace Emissor.Modules.Nfe.Infrastructure.Xml;

/// <summary>
/// Valida o XML contra os schemas oficiais embutidos no assembly. A SEFAZ faz
/// exatamente esta validacao ao receber o lote; fazer aqui antes poupa uma
/// ida a rede e devolve a mensagem do schema, que aponta o campo errado.
/// </summary>
public sealed class NfeXsdValidator : INfeXsdValidator
{
    public const string NamespaceNfe = "http://www.portalfiscal.inf.br/nfe";
    private const string SchemaNfe = "nfe_v4.00.xsd";
    private const string SchemaCancelamento = "envEventoCancNFe_v1.00.xsd";
    private const string SchemaCartaCorrecao = "envCCe_v1.00.xsd";
    private const string SchemaInutilizacao = "inutNFe_v4.00.xsd";

    // Um conjunto por schema raiz: cancelamento e carta definem o mesmo elemento envEvento com tipos diferentes.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<XmlSchemaSet>> Schemas = new(StringComparer.Ordinal);

    public Result ValidarNfe(string xml) => Validar(xml, SchemaNfe);

    public Result ValidarEvento(string xmlEnvEvento, Contracts.Nfe.TipoEventoNfe tipo) =>
        Validar(xmlEnvEvento, tipo == Contracts.Nfe.TipoEventoNfe.Cancelamento ? SchemaCancelamento : SchemaCartaCorrecao);

    public Result ValidarInutilizacao(string xmlInutNfe) => Validar(xmlInutNfe, SchemaInutilizacao);

    private static Result Validar(string xml, string schemaRaiz)
    {
        var erros = new List<string>();

        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = Schemas.GetOrAdd(schemaRaiz, raiz => new Lazy<XmlSchemaSet>(() => CarregarSchemas(raiz), LazyThreadSafetyMode.ExecutionAndPublication)).Value,
            ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings,
            XmlResolver = null,
            DtdProcessing = DtdProcessing.Prohibit
        };

        settings.ValidationEventHandler += (_, e) =>
            erros.Add(e.Exception is { LineNumber: > 0 } ex
                ? $"Linha {ex.LineNumber}, coluna {ex.LinePosition}: {e.Message}"
                : e.Message);

        try
        {
            using var leitor = XmlReader.Create(new StringReader(xml), settings);

            while (leitor.Read())
            {
            }
        }
        catch (XmlException ex)
        {
            erros.Add($"XML mal formado: {ex.Message}");
        }

        return erros.Count == 0
            ? Result.Success()
            : Result.Failure(NfeErrors.XmlInvalido.ComDetalhes(new Dictionary<string, string[]> { ["xml"] = erros.ToArray() }));
    }

    private static XmlSchemaSet CarregarSchemas(string schemaRaiz)
    {
        var set = new XmlSchemaSet { XmlResolver = new EmbeddedSchemaResolver() };

        using var raiz = AbrirSchema(schemaRaiz);
        set.Add(NamespaceNfe, XmlReader.Create(raiz, new XmlReaderSettings { XmlResolver = new EmbeddedSchemaResolver() }, "schemas:///" + schemaRaiz));
        set.Compile();

        return set;
    }

    private static Stream AbrirSchema(string nome) =>
        typeof(NfeXsdValidator).Assembly.GetManifestResourceStream("Schemas/" + nome)
        ?? throw new InvalidOperationException($"Schema embutido não encontrado: {nome}");

    /// <summary>Resolve os <c>xs:include</c>/<c>xs:import</c> relativos para os recursos embutidos.</summary>
    private sealed class EmbeddedSchemaResolver : XmlResolver
    {
        public override Uri ResolveUri(Uri? baseUri, string? relativeUri) =>
            new("schemas:///" + Path.GetFileName(relativeUri ?? string.Empty));

        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn) =>
            AbrirSchema(Path.GetFileName(absoluteUri.AbsolutePath));
    }
}
