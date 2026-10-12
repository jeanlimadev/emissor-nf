using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Emissor.Modules.Emitentes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Contingencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "contingencia_desde",
                schema: "emitentes",
                table: "emitentes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contingencia_justificativa",
                schema: "emitentes",
                table: "emitentes",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "contingencia_desde",
                schema: "emitentes",
                table: "emitentes");

            migrationBuilder.DropColumn(
                name: "contingencia_justificativa",
                schema: "emitentes",
                table: "emitentes");
        }
    }
}
