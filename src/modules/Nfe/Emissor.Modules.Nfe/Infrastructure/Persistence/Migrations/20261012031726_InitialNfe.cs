using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Emissor.Modules.Nfe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialNfe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "nfe");

            migrationBuilder.CreateTable(
                name: "notas",
                schema: "nfe",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    emitente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ambiente = table.Column<int>(type: "integer", nullable: false),
                    chave_idempotencia = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    pedido_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    pedido_json = table.Column<string>(type: "jsonb", nullable: false),
                    serie = table.Column<int>(type: "integer", nullable: false),
                    numero = table.Column<long>(type: "bigint", nullable: true),
                    chave_acesso = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    data_emissao = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xml_assinado = table.Column<string>(type: "text", nullable: true),
                    xml_proc_nfe = table.Column<string>(type: "text", nullable: true),
                    protocolo = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    recibo = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    c_stat = table.Column<int>(type: "integer", nullable: true),
                    x_motivo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    data_autorizacao = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    proxima_tentativa_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notas", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notas_chave_acesso",
                schema: "nfe",
                table: "notas",
                column: "chave_acesso",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notas_status_proxima_tentativa_em",
                schema: "nfe",
                table: "notas",
                columns: new[] { "status", "proxima_tentativa_em" });

            migrationBuilder.CreateIndex(
                name: "IX_notas_tenant_id_emitente_id_chave_idempotencia",
                schema: "nfe",
                table: "notas",
                columns: new[] { "tenant_id", "emitente_id", "chave_idempotencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notas_tenant_id_emitente_id_criada_em",
                schema: "nfe",
                table: "notas",
                columns: new[] { "tenant_id", "emitente_id", "criada_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notas",
                schema: "nfe");
        }
    }
}
