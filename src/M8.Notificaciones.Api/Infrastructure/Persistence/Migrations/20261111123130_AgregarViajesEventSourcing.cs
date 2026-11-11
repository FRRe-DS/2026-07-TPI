using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace M8.Notificaciones.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarViajesEventSourcing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventosViaje",
                columns: table => new
                {
                    Seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViajeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Origen = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    OcurridoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RegistradoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosViaje", x => x.Seq);
                });

            migrationBuilder.CreateTable(
                name: "ProyeccionCheckpoint",
                columns: table => new
                {
                    Nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UltimaSeq = table.Column<long>(type: "bigint", nullable: false),
                    ActualizadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProyeccionCheckpoint", x => x.Nombre);
                });

            migrationBuilder.CreateTable(
                name: "ViajesHistorialVista",
                columns: table => new
                {
                    ViajeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Evento = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Descripcion = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    EstadoResultante = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConductorId = table.Column<Guid>(type: "uuid", nullable: true),
                    OcurridoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RegistradoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProyectadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ViajesHistorialVista", x => new { x.ViajeId, x.Version });
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventosViaje_EventoId",
                table: "EventosViaje",
                column: "EventoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventosViaje_ViajeId_Version",
                table: "EventosViaje",
                columns: new[] { "ViajeId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventosViaje");

            migrationBuilder.DropTable(
                name: "ProyeccionCheckpoint");

            migrationBuilder.DropTable(
                name: "ViajesHistorialVista");
        }
    }
}
