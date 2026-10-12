using Emissor.BuildingBlocks;
using Emissor.Modules.Nfe.Domain;
using Microsoft.EntityFrameworkCore;

namespace Emissor.Modules.Nfe.Infrastructure.Persistence;

/// <summary>
/// Banco do modulo, no schema <c>nfe</c>. A tabela de notas e tambem a fila
/// de transmissao: status de fila + <c>proxima_tentativa_em</c> vencido.
/// </summary>
public sealed class NfeDbContext(DbContextOptions<NfeDbContext> options, ICurrentTenant tenant) : DbContext(options)
{
    public const string Schema = "nfe";
    public const string TabelaNotas = "notas";

    private readonly ICurrentTenant _tenant = tenant;

    private Guid TenantId => _tenant.TenantId;

    public DbSet<NotaFiscal> Notas => Set<NotaFiscal>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<WebhookEntrega> WebhookEntregas => Set<WebhookEntrega>();
    public DbSet<EventoNfe> Eventos => Set<EventoNfe>();
    public DbSet<Inutilizacao> Inutilizacoes => Set<Inutilizacao>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<NotaFiscal>(nota =>
        {
            nota.ToTable(TabelaNotas);
            nota.HasKey(n => n.Id);
            nota.Property(n => n.Id).HasColumnName("id");
            nota.Property(n => n.TenantId).HasColumnName("tenant_id");
            nota.Property(n => n.EmitenteId).HasColumnName("emitente_id");
            nota.Property(n => n.Ambiente).HasColumnName("ambiente");
            nota.Property(n => n.ChaveIdempotencia).HasColumnName("chave_idempotencia").HasMaxLength(NotaFiscal.ChaveIdempotenciaMaxLength).IsRequired();
            nota.Property(n => n.PedidoHash).HasColumnName("pedido_hash").HasMaxLength(64).IsRequired();
            nota.Property(n => n.PedidoJson).HasColumnName("pedido_json").HasColumnType("jsonb").IsRequired();
            nota.Property(n => n.Serie).HasColumnName("serie");
            nota.Property(n => n.Numero).HasColumnName("numero");
            nota.Property(n => n.ChaveAcesso).HasColumnName("chave_acesso").HasMaxLength(ChaveAcesso.Tamanho);
            nota.Property(n => n.Status).HasColumnName("status");
            nota.Property(n => n.DataEmissao).HasColumnName("data_emissao");
            nota.Property(n => n.XmlAssinado).HasColumnName("xml_assinado");
            nota.Property(n => n.XmlProcNfe).HasColumnName("xml_proc_nfe");
            nota.Property(n => n.Protocolo).HasColumnName("protocolo").HasMaxLength(15);
            nota.Property(n => n.Recibo).HasColumnName("recibo").HasMaxLength(15);
            nota.Property(n => n.CStat).HasColumnName("c_stat");
            nota.Property(n => n.XMotivo).HasColumnName("x_motivo").HasMaxLength(NotaFiscal.XMotivoMaxLength);
            nota.Property(n => n.DataAutorizacao).HasColumnName("data_autorizacao");
            nota.Property(n => n.Tentativas).HasColumnName("tentativas");
            nota.Property(n => n.ProximaTentativaEm).HasColumnName("proxima_tentativa_em");
            nota.Property(n => n.CriadaEm).HasColumnName("criada_em");
            nota.Property(n => n.AtualizadaEm).HasColumnName("atualizada_em");

            nota.HasIndex(n => new { n.TenantId, n.EmitenteId, n.ChaveIdempotencia }).IsUnique();
            nota.HasIndex(n => n.ChaveAcesso).IsUnique();
            nota.HasIndex(n => new { n.Status, n.ProximaTentativaEm });
            nota.HasIndex(n => new { n.TenantId, n.EmitenteId, n.CriadaEm });
            nota.HasQueryFilter(n => n.TenantId == TenantId);
        });

        modelBuilder.Entity<Webhook>(webhook =>
        {
            webhook.ToTable("webhooks");
            webhook.HasKey(w => w.Id);
            webhook.Property(w => w.Id).HasColumnName("id");
            webhook.Property(w => w.TenantId).HasColumnName("tenant_id");
            webhook.Property(w => w.Url).HasColumnName("url").HasMaxLength(Webhook.UrlMaxLength).IsRequired();
            webhook.Property(w => w.SegredoProtegido).HasColumnName("segredo_protegido").IsRequired();
            webhook.Property(w => w.Ativo).HasColumnName("ativo");
            webhook.Property(w => w.CriadoEm).HasColumnName("criado_em");
            webhook.Property(w => w.AtualizadoEm).HasColumnName("atualizado_em");
            webhook.HasIndex(w => w.TenantId).IsUnique();
            webhook.HasQueryFilter(w => w.TenantId == TenantId);
        });

        modelBuilder.Entity<WebhookEntrega>(entrega =>
        {
            entrega.ToTable("webhook_entregas");
            entrega.HasKey(e => e.Id);
            entrega.Property(e => e.Id).HasColumnName("id");
            entrega.Property(e => e.TenantId).HasColumnName("tenant_id");
            entrega.Property(e => e.WebhookId).HasColumnName("webhook_id");
            entrega.Property(e => e.NfeId).HasColumnName("nfe_id");
            entrega.Property(e => e.Evento).HasColumnName("evento").HasMaxLength(40).IsRequired();
            entrega.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            entrega.Property(e => e.Status).HasColumnName("status");
            entrega.Property(e => e.Tentativas).HasColumnName("tentativas");
            entrega.Property(e => e.UltimoHttpStatus).HasColumnName("ultimo_http_status");
            entrega.Property(e => e.UltimoErro).HasColumnName("ultimo_erro").HasMaxLength(WebhookEntrega.ErroMaxLength);
            entrega.Property(e => e.ProximaTentativaEm).HasColumnName("proxima_tentativa_em");
            entrega.Property(e => e.CriadaEm).HasColumnName("criada_em");
            entrega.Property(e => e.EntregueEm).HasColumnName("entregue_em");
            entrega.HasIndex(e => new { e.Status, e.ProximaTentativaEm });
            entrega.HasIndex(e => new { e.TenantId, e.NfeId, e.CriadaEm });
            entrega.HasQueryFilter(e => e.TenantId == TenantId);
        });

        modelBuilder.Entity<EventoNfe>(evento =>
        {
            evento.ToTable("eventos");
            evento.HasKey(e => e.Id);
            evento.Property(e => e.Id).HasColumnName("id");
            evento.Property(e => e.TenantId).HasColumnName("tenant_id");
            evento.Property(e => e.NfeId).HasColumnName("nfe_id");
            evento.Property(e => e.Tipo).HasColumnName("tipo");
            evento.Property(e => e.Sequencia).HasColumnName("sequencia");
            evento.Property(e => e.Texto).HasColumnName("texto").HasMaxLength(EventoNfe.CorrecaoMax).IsRequired();
            evento.Property(e => e.Status).HasColumnName("status");
            evento.Property(e => e.DataEvento).HasColumnName("data_evento");
            evento.Property(e => e.XmlEvento).HasColumnName("xml_evento");
            evento.Property(e => e.XmlProcEvento).HasColumnName("xml_proc_evento");
            evento.Property(e => e.Protocolo).HasColumnName("protocolo").HasMaxLength(15);
            evento.Property(e => e.CStat).HasColumnName("c_stat");
            evento.Property(e => e.XMotivo).HasColumnName("x_motivo").HasMaxLength(NotaFiscal.XMotivoMaxLength);
            evento.Property(e => e.RegistradoEm).HasColumnName("registrado_em");
            evento.Property(e => e.CriadoEm).HasColumnName("criado_em");
            evento.HasOne<NotaFiscal>().WithMany().HasForeignKey(e => e.NfeId).OnDelete(DeleteBehavior.Cascade);
            evento.HasIndex(e => new { e.NfeId, e.Tipo, e.Sequencia }).IsUnique();
            evento.HasQueryFilter(e => e.TenantId == TenantId);
        });

        modelBuilder.Entity<Inutilizacao>(inut =>
        {
            inut.ToTable("inutilizacoes");
            inut.HasKey(i => i.Id);
            inut.Property(i => i.Id).HasColumnName("id");
            inut.Property(i => i.TenantId).HasColumnName("tenant_id");
            inut.Property(i => i.EmitenteId).HasColumnName("emitente_id");
            inut.Property(i => i.Serie).HasColumnName("serie");
            inut.Property(i => i.NumeroInicial).HasColumnName("numero_inicial");
            inut.Property(i => i.NumeroFinal).HasColumnName("numero_final");
            inut.Property(i => i.Ano).HasColumnName("ano");
            inut.Property(i => i.Justificativa).HasColumnName("justificativa").HasMaxLength(EventoNfe.JustificativaMax).IsRequired();
            inut.Property(i => i.Status).HasColumnName("status");
            inut.Property(i => i.XmlInutilizacao).HasColumnName("xml_inutilizacao");
            inut.Property(i => i.XmlProcInutilizacao).HasColumnName("xml_proc_inutilizacao");
            inut.Property(i => i.Protocolo).HasColumnName("protocolo").HasMaxLength(15);
            inut.Property(i => i.CStat).HasColumnName("c_stat");
            inut.Property(i => i.XMotivo).HasColumnName("x_motivo").HasMaxLength(NotaFiscal.XMotivoMaxLength);
            inut.Property(i => i.HomologadaEm).HasColumnName("homologada_em");
            inut.Property(i => i.CriadaEm).HasColumnName("criada_em");
            inut.HasIndex(i => new { i.TenantId, i.EmitenteId, i.CriadaEm });
            inut.HasQueryFilter(i => i.TenantId == TenantId);
        });
    }
}
