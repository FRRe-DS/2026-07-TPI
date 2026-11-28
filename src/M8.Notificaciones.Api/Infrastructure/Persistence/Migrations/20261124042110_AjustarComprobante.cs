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
            migrationBuilder.RenameColumn(
                name: "RutaArchivoPdf",
                table: "Comprobantes",
                newName: "UrlPdf");

            migrationBuilder.RenameColumn(
                name: "FechaEmision",
                table: "Comprobantes",
                newName: "CreadoEn");

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

            migrationBuilder.AddColumn<DateTime>(
                name: "EmitidoEn",
                table: "Comprobantes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Estado",
                table: "Comprobantes",
                type: "text",
                nullable: false,
                defaultValue: "PENDIENTE");

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
                name: "EmitidoEn",
                table: "Comprobantes");

            migrationBuilder.DropColumn(
                name: "Estado",
                table: "Comprobantes");

            migrationBuilder.DropColumn(
                name: "Motivo",
                table: "Comprobantes");

            migrationBuilder.DropColumn(
                name: "PagoId",
                table: "Comprobantes");

            migrationBuilder.RenameColumn(
                name: "UrlPdf",
                table: "Comprobantes",
                newName: "RutaArchivoPdf");

            migrationBuilder.RenameColumn(
                name: "CreadoEn",
                table: "Comprobantes",
                newName: "FechaEmision");

            migrationBuilder.Sql("""
                ALTER TABLE "Comprobantes"
                ALTER COLUMN "ViajeId" TYPE character varying(100)
                USING "ViajeId"::character varying(100);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "Comprobantes"
                ALTER COLUMN "ClienteId" TYPE character varying(100)
                USING "ClienteId"::character varying(100);
                """);
        }
    }
}
