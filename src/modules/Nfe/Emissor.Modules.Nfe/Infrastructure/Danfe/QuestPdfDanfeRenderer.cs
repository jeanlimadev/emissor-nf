using Emissor.Modules.Nfe.Application.Ports;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Emissor.Modules.Nfe.Infrastructure.Danfe;

/// <summary>
/// Gera a DANFE em PDF com o QuestPDF (licenca Community, ADR-019). Le o XML
/// com o <see cref="DanfeLeitor"/> e desenha com o <see cref="DanfeDocument"/>.
/// </summary>
public sealed class QuestPdfDanfeRenderer : IDanfeRenderer
{
    static QuestPdfDanfeRenderer()
    {
        // Exigencia da licenca: declarar o tipo antes do primeiro uso.
        QuestPDF.Settings.License = LicenseType.Community;
        // A fonte padrao (Lato) vem embutida na biblioteca: nada a instalar no container.
        QuestPDF.Settings.UseSystemFonts = false;
        // Erro de leiaute aponta o elemento culpado em vez de uma mensagem generica.
        QuestPDF.Settings.EnableDetailedLayoutErrors = true;
    }

    public byte[] Renderizar(string xml, string? marca)
    {
        var dados = DanfeLeitor.Ler(xml);

        return new DanfeDocument(dados, marca).GeneratePdf();
    }
}
