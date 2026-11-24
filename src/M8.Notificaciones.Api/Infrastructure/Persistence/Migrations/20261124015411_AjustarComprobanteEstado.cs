using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace M8.Notificaciones.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AjustarComprobanteEstado : Migration
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

            migrationBuilder.AlterColumn<string>(
                name: "Estado",
                table: "Comprobantes",
                type: "text",
                nullable: false,
                defaultValue: "PENDIENTE",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<DateTime>(
                name: "EmitidoEn",
                table: "Comprobantes",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmitidoEn",
                table: "Comprobantes");

            migrationBuilder.RenameColumn(
                name: "UrlPdf",
                table: "Comprobantes",
                newName: "RutaArchivoPdf");

            migrationBuilder.RenameColumn(
                name: "CreadoEn",
                table: "Comprobantes",
                newName: "FechaEmision");

            migrationBuilder.AlterColumn<string>(
                name: "Estado",
                table: "Comprobantes",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "PENDIENTE");
        }
    }
}
