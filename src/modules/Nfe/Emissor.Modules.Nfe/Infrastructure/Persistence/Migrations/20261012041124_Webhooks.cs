using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Emissor.Modules.Nfe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Webhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhook_entregas",
                schema: "nfe",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    webhook_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nfe_id = table.Column<Guid>(type: "uuid", nullable: true),
                    evento = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    ultimo_http_status = table.Column<int>(type: "integer", nullable: true),
                    ultimo_erro = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    proxima_tentativa_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    entregue_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_entregas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhooks",
                schema: "nfe",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    segredo_protegido = table.Column<byte[]>(type: "bytea", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhooks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_entregas_status_proxima_tentativa_em",
                schema: "nfe",
                table: "webhook_entregas",
                columns: new[] { "status", "proxima_tentativa_em" });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_entregas_tenant_id_nfe_id_criada_em",
                schema: "nfe",
                table: "webhook_entregas",
                columns: new[] { "tenant_id", "nfe_id", "criada_em" });

            migrationBuilder.CreateIndex(
                name: "IX_webhooks_tenant_id",
                schema: "nfe",
                table: "webhooks",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_entregas",
                schema: "nfe");

            migrationBuilder.DropTable(
                name: "webhooks",
                schema: "nfe");
        }
    }
}
