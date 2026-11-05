using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace M8.Notificaciones.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AjustarComprobante : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Comprobantes"
                ALTER COLUMN "ViajeId" TYPE uuid
                USING "ViajeId"::uuid;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE "Comprobantes"
                ALTER COLUMN "ClienteId" TYPE uuid
                USING "ClienteId"::uuid;
            """);

            migrationBuilder.AddColumn<string>(
                name: "Estado",
                table: "Comprobantes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Motivo",
                table: "Comprobantes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "PagoId",
                table: "Comprobantes",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Estado",
                table: "Comprobantes");

            migrationBuilder.DropColumn(
                name: "Motivo",
                table: "Comprobantes");

            migrationBuilder.DropColumn(
                name: "PagoId",
                table: "Comprobantes");

            migrationBuilder.AlterColumn<string>(
                name: "ViajeId",
                table: "Comprobantes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "ClienteId",
                table: "Comprobantes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldMaxLength: 100);
        }
    }
}
