using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace M8.Notificaciones.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SoporteTicketsV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TP1 permitía IDs libres y no registraba el rol. Su tabla de prueba queda intacta.
            migrationBuilder.CreateTable(
                name: "TicketsSoporte",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rol = table.Column<string>(type: "text", nullable: false),
                    ReferenciaTipo = table.Column<string>(type: "text", nullable: false),
                    ReferenciaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Titulo = table.Column<string>(type: "text", nullable: false),
                    Descripcion = table.Column<string>(type: "text", nullable: false),
                    ClaveIdempotencia = table.Column<string>(type: "text", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    Resolucion = table.Column<string>(type: "text", nullable: true),
                    CreadoEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActualizadoEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CerradoEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_TicketsSoporte", x => x.Id));

            migrationBuilder.CreateTable(
                name: "HistorialTickets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstadoAnterior = table.Column<string>(type: "text", nullable: true),
                    EstadoNuevo = table.Column<string>(type: "text", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    Motivo = table.Column<string>(type: "text", nullable: true),
                    CambiadoEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistorialTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistorialTickets_TicketsSoporte_TicketId",
                        column: x => x.TicketId,
                        principalTable: "TicketsSoporte",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TicketsSoporte_CreadoEn",
                table: "TicketsSoporte",
                column: "CreadoEn");

            migrationBuilder.CreateIndex(
                name: "IX_TicketsSoporte_ReferenciaTipo_ReferenciaId",
                table: "TicketsSoporte",
                columns: new[] { "ReferenciaTipo", "ReferenciaId" });

            migrationBuilder.CreateIndex(
                name: "UX_TicketsSoporte_Usuario_Clave",
                table: "TicketsSoporte",
                columns: new[] { "UsuarioId", "ClaveIdempotencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HistorialTickets_TicketId_Id",
                table: "HistorialTickets",
                columns: new[] { "TicketId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HistorialTickets");

            migrationBuilder.DropTable(name: "TicketsSoporte");
        }
    }
}
