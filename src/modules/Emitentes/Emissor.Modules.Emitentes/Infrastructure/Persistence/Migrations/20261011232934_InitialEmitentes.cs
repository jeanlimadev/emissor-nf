using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Emissor.Modules.Emitentes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialEmitentes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "emitentes");

            migrationBuilder.CreateTable(
                name: "emitentes",
                schema: "emitentes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    documento = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    inscricao_estadual = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    nome = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    nome_fantasia = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    crt = table.Column<int>(type: "integer", nullable: false),
                    ambiente = table.Column<int>(type: "integer", nullable: false),
                    end_logradouro = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    end_numero = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    end_complemento = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    end_bairro = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    end_codigo_municipio = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    end_municipio = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    end_uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    end_cep = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    end_telefone = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    email = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_emitentes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "certificados",
                schema: "emitentes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    emitente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    titular = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    documento = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    thumbprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    valido_de = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valido_ate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pfx_protegido = table.Column<byte[]>(type: "bytea", nullable: false),
                    senha_protegida = table.Column<byte[]>(type: "bytea", nullable: false),
                    enviado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_certificados", x => x.id);
                    table.ForeignKey(
                        name: "FK_certificados_emitentes_emitente_id",
                        column: x => x.emitente_id,
                        principalSchema: "emitentes",
                        principalTable: "emitentes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "series",
                schema: "emitentes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    emitente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modelo = table.Column<int>(type: "integer", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    proximo_numero = table.Column<long>(type: "bigint", nullable: false),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series", x => x.id);
                    table.ForeignKey(
                        name: "FK_series_emitentes_emitente_id",
                        column: x => x.emitente_id,
                        principalSchema: "emitentes",
                        principalTable: "emitentes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_certificados_emitente_id",
                schema: "emitentes",
                table: "certificados",
                column: "emitente_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_certificados_tenant_id_valido_ate",
                schema: "emitentes",
                table: "certificados",
                columns: new[] { "tenant_id", "valido_ate" });

            migrationBuilder.CreateIndex(
                name: "IX_emitentes_tenant_id_ativo_nome_id",
                schema: "emitentes",
                table: "emitentes",
                columns: new[] { "tenant_id", "ativo", "nome", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_emitentes_tenant_id_documento",
                schema: "emitentes",
                table: "emitentes",
                columns: new[] { "tenant_id", "documento" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_series_emitente_id_modelo_numero",
                schema: "emitentes",
                table: "series",
                columns: new[] { "emitente_id", "modelo", "numero" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "certificados",
                schema: "emitentes");

            migrationBuilder.DropTable(
                name: "series",
                schema: "emitentes");

            migrationBuilder.DropTable(
                name: "emitentes",
                schema: "emitentes");
        }
    }
}
