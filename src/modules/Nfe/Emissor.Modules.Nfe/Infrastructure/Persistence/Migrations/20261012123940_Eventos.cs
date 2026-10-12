using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Emissor.Modules.Nfe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Eventos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "eventos",
                schema: "nfe",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nfe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    sequencia = table.Column<int>(type: "integer", nullable: false),
                    texto = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    data_evento = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xml_evento = table.Column<string>(type: "text", nullable: true),
                    xml_proc_evento = table.Column<string>(type: "text", nullable: true),
                    protocolo = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    c_stat = table.Column<int>(type: "integer", nullable: true),
                    x_motivo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    registrado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eventos", x => x.id);
                    table.ForeignKey(
                        name: "FK_eventos_notas_nfe_id",
                        column: x => x.nfe_id,
                        principalSchema: "nfe",
                        principalTable: "notas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inutilizacoes",
                schema: "nfe",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    emitente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serie = table.Column<int>(type: "integer", nullable: false),
                    numero_inicial = table.Column<long>(type: "bigint", nullable: false),
                    numero_final = table.Column<long>(type: "bigint", nullable: false),
                    ano = table.Column<int>(type: "integer", nullable: false),
                    justificativa = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    xml_inutilizacao = table.Column<string>(type: "text", nullable: true),
                    xml_proc_inutilizacao = table.Column<string>(type: "text", nullable: true),
                    protocolo = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    c_stat = table.Column<int>(type: "integer", nullable: true),
                    x_motivo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    homologada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inutilizacoes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_eventos_nfe_id_tipo_sequencia",
                schema: "nfe",
                table: "eventos",
                columns: new[] { "nfe_id", "tipo", "sequencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inutilizacoes_tenant_id_emitente_id_criada_em",
                schema: "nfe",
                table: "inutilizacoes",
                columns: new[] { "tenant_id", "emitente_id", "criada_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "eventos",
                schema: "nfe");

            migrationBuilder.DropTable(
                name: "inutilizacoes",
                schema: "nfe");
        }
    }
}
