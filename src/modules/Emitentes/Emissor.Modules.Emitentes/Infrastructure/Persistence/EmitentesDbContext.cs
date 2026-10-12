using Emissor.BuildingBlocks;
using Emissor.Modules.Emitentes.Domain;
using Microsoft.EntityFrameworkCore;

namespace Emissor.Modules.Emitentes.Infrastructure.Persistence;

/// <summary>
/// Banco do modulo, no schema <c>emitentes</c>. Toda entidade tem
/// <c>tenant_id</c> e o filtro global abaixo: uma consulta sem tenant amarrado
/// falha em vez de devolver o cadastro de todo mundo.
/// </summary>
public sealed class EmitentesDbContext(DbContextOptions<EmitentesDbContext> options, ICurrentTenant tenant) : DbContext(options)
{
    public const string Schema = "emitentes";

    private readonly ICurrentTenant _tenant = tenant;

    // Propriedade (e nao campo) para o EF parametrizar o filtro por requisicao.
    private Guid TenantId => _tenant.TenantId;

    public DbSet<Emitente> Emitentes => Set<Emitente>();
    public DbSet<Certificado> Certificados => Set<Certificado>();
    public DbSet<Serie> Series => Set<Serie>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Emitente>(emitente =>
        {
            emitente.ToTable("emitentes");
            emitente.HasKey(e => e.Id);
            emitente.Property(e => e.Id).HasColumnName("id");
            emitente.Property(e => e.TenantId).HasColumnName("tenant_id");
            emitente.Property(e => e.Tipo).HasColumnName("tipo");
            emitente.Property(e => e.Documento).HasColumnName("documento").HasMaxLength(14).IsRequired();
            emitente.Property(e => e.InscricaoEstadual).HasColumnName("inscricao_estadual").HasMaxLength(Emitente.InscricaoEstadualMaxLength).IsRequired();
            emitente.Property(e => e.Nome).HasColumnName("nome").HasMaxLength(Emitente.NomeMaxLength).IsRequired();
            emitente.Property(e => e.NomeFantasia).HasColumnName("nome_fantasia").HasMaxLength(Emitente.NomeFantasiaMaxLength);
            emitente.Property(e => e.RegimeTributario).HasColumnName("crt");
            emitente.Property(e => e.Ambiente).HasColumnName("ambiente");
            emitente.Property(e => e.Email).HasColumnName("email").HasMaxLength(Emitente.EmailMaxLength);
            emitente.Property(e => e.Ativo).HasColumnName("ativo");
            emitente.Property(e => e.CriadoEm).HasColumnName("criado_em");
            emitente.Property(e => e.AtualizadoEm).HasColumnName("atualizado_em");
            emitente.Property(e => e.ContingenciaDesde).HasColumnName("contingencia_desde");
            emitente.Property(e => e.ContingenciaJustificativa).HasColumnName("contingencia_justificativa").HasMaxLength(Emitente.JustificativaContingenciaMax);
            emitente.Ignore(e => e.EmContingencia);

            emitente.OwnsOne(e => e.Endereco, endereco =>
            {
                endereco.Property(a => a.Logradouro).HasColumnName("end_logradouro").HasMaxLength(Endereco.LogradouroMaxLength).IsRequired();
                endereco.Property(a => a.Numero).HasColumnName("end_numero").HasMaxLength(Endereco.NumeroMaxLength).IsRequired();
                endereco.Property(a => a.Complemento).HasColumnName("end_complemento").HasMaxLength(Endereco.ComplementoMaxLength);
                endereco.Property(a => a.Bairro).HasColumnName("end_bairro").HasMaxLength(Endereco.BairroMaxLength).IsRequired();
                endereco.Property(a => a.CodigoMunicipio).HasColumnName("end_codigo_municipio").HasMaxLength(7).IsRequired();
                endereco.Property(a => a.Municipio).HasColumnName("end_municipio").HasMaxLength(Endereco.MunicipioMaxLength).IsRequired();
                endereco.Property(a => a.Uf).HasColumnName("end_uf").HasMaxLength(2).IsRequired();
                endereco.Property(a => a.Cep).HasColumnName("end_cep").HasMaxLength(8).IsRequired();
                endereco.Property(a => a.Telefone).HasColumnName("end_telefone").HasMaxLength(14);
            });

            emitente.HasOne(e => e.Certificado)
                .WithOne()
                .HasForeignKey<Certificado>(c => c.EmitenteId)
                .OnDelete(DeleteBehavior.Cascade);

            emitente.Navigation(e => e.Certificado).AutoInclude();

            emitente.HasIndex(e => new { e.TenantId, e.Documento }).IsUnique();
            emitente.HasIndex(e => new { e.TenantId, e.Ativo, e.Nome, e.Id });
            emitente.HasQueryFilter(e => e.TenantId == TenantId);
        });

        modelBuilder.Entity<Certificado>(certificado =>
        {
            certificado.ToTable("certificados");
            certificado.HasKey(c => c.Id);
            certificado.Property(c => c.Id).HasColumnName("id");
            certificado.Property(c => c.TenantId).HasColumnName("tenant_id");
            certificado.Property(c => c.EmitenteId).HasColumnName("emitente_id");
            certificado.Property(c => c.Titular).HasColumnName("titular").HasMaxLength(200).IsRequired();
            certificado.Property(c => c.Documento).HasColumnName("documento").HasMaxLength(14).IsRequired();
            certificado.Property(c => c.Thumbprint).HasColumnName("thumbprint").HasMaxLength(64).IsRequired();
            certificado.Property(c => c.ValidoDe).HasColumnName("valido_de");
            certificado.Property(c => c.ValidoAte).HasColumnName("valido_ate");
            certificado.Property(c => c.PfxProtegido).HasColumnName("pfx_protegido").IsRequired();
            certificado.Property(c => c.SenhaProtegida).HasColumnName("senha_protegida").IsRequired();
            certificado.Property(c => c.EnviadoEm).HasColumnName("enviado_em");
            certificado.HasIndex(c => c.EmitenteId).IsUnique();
            certificado.HasIndex(c => new { c.TenantId, c.ValidoAte });
            certificado.HasQueryFilter(c => c.TenantId == TenantId);
        });

        modelBuilder.Entity<Serie>(serie =>
        {
            serie.ToTable("series");
            serie.HasKey(s => s.Id);
            serie.Property(s => s.Id).HasColumnName("id");
            serie.Property(s => s.TenantId).HasColumnName("tenant_id");
            serie.Property(s => s.EmitenteId).HasColumnName("emitente_id");
            serie.Property(s => s.Modelo).HasColumnName("modelo");
            serie.Property(s => s.Numero).HasColumnName("numero");
            serie.Property(s => s.ProximoNumero).HasColumnName("proximo_numero");
            serie.Property(s => s.Ativa).HasColumnName("ativa");
            serie.Property(s => s.CriadaEm).HasColumnName("criada_em");
            serie.HasOne<Emitente>().WithMany().HasForeignKey(s => s.EmitenteId).OnDelete(DeleteBehavior.Cascade);
            serie.HasIndex(s => new { s.EmitenteId, s.Modelo, s.Numero }).IsUnique();
            serie.HasQueryFilter(s => s.TenantId == TenantId);
        });
    }
}
