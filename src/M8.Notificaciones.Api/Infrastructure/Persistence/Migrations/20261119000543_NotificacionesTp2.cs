using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace M8.Notificaciones.Api.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// TP2 - Notificaciones según el contrato de M8.
    ///
    /// Editada a mano: EF proponía renombrar y convertir columnas de la tabla
    /// "Notificaciones" del TP1 (por ejemplo ReferenciaId de texto a uuid), lo que
    /// falla si hay datos de prueba que no son UUID. La tabla del TP1 tenía otra
    /// forma (Destinatario como texto libre, sin destinatarioId ni rol) y solo
    /// datos de prueba, así que se elimina y se crea la nueva. Down la restaura.
    /// </summary>
    public partial class NotificacionesTp2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notificaciones");

            migrationBuilder.CreateTable(
                name: "Notificaciones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventoId = table.Column<Guid>(type: "uuid", nullable: true),
                    TipoEvento = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DestinatarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Canal = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Contacto = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Asunto = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Mensaje = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ReferenciaTipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ReferenciaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Intentos = table.Column<int>(type: "integer", nullable: false),
                    MaxIntentos = table.Column<int>(type: "integer", nullable: false),
                    ProximoIntentoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BloqueadaHasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EnviadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UltimoError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notificaciones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContactosDestinatarios",
                columns: table => new
                {
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Telefono = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ActualizadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactosDestinatarios", x => x.UsuarioId);
                });

            migrationBuilder.CreateTable(
                name: "EventosRecibidos",
                columns: table => new
                {
                    EventoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Origen = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    HashCuerpo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Via = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    OcurridoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecibidoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NotificacionesGeneradas = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosRecibidos", x => x.EventoId);
                });

            migrationBuilder.CreateTable(
                name: "NotificacionIntentos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NotificacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Numero = table.Column<int>(type: "integer", nullable: false),
                    Canal = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Resultado = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RealizadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificacionIntentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificacionIntentos_Notificaciones_NotificacionId",
                        column: x => x.NotificacionId,
                        principalTable: "Notificaciones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notificaciones_CreadoEn",
                table: "Notificaciones",
                column: "CreadoEn");

            migrationBuilder.CreateIndex(
                name: "IX_Notificaciones_DestinatarioId",
                table: "Notificaciones",
                column: "DestinatarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Notificaciones_Estado_ProximoIntentoEn",
                table: "Notificaciones",
                columns: new[] { "Estado", "ProximoIntentoEn" });

            migrationBuilder.CreateIndex(
                name: "IX_Notificaciones_EventoId_DestinatarioId_Rol",
                table: "Notificaciones",
                columns: new[] { "EventoId", "DestinatarioId", "Rol" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notificaciones_ReferenciaId",
                table: "Notificaciones",
                column: "ReferenciaId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificacionIntentos_NotificacionId_Numero",
                table: "NotificacionIntentos",
                columns: new[] { "NotificacionId", "Numero" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContactosDestinatarios");

            migrationBuilder.DropTable(
                name: "EventosRecibidos");

            migrationBuilder.DropTable(
                name: "NotificacionIntentos");

            migrationBuilder.DropTable(
                name: "Notificaciones");

            // Tabla del TP1, igual que en InitialCreate.
            migrationBuilder.CreateTable(
                name: "Notificaciones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Destinatario = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Canal = table.Column<string>(type: "text", nullable: false),
                    Asunto = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Mensaje = table.Column<string>(type: "text", nullable: false),
                    ReferenciaId = table.Column<string>(type: "text", nullable: true),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    IntentosReenvio = table.Column<int>(type: "integer", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaEnvio = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorUltimoIntento = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notificaciones", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notificaciones_Destinatario",
                table: "Notificaciones",
                column: "Destinatario");

            migrationBuilder.CreateIndex(
                name: "IX_Notificaciones_Estado",
                table: "Notificaciones",
                column: "Estado");
        }
    }
}
