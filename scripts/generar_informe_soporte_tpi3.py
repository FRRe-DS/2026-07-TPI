import sys
import os
from reportlab.lib.pagesizes import letter
from reportlab.lib import colors
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, PageBreak, KeepTogether, HRFlowable
)
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.pdfgen import canvas

class NumberedCanvas(canvas.Canvas):
    def __init__(self, *args, **kwargs):
        super(NumberedCanvas, self).__init__(*args, **kwargs)
        self._saved_page_states = []

    def showPage(self):
        self._saved_page_states.append(dict(self.__dict__))
        self._startPage()

    def save(self):
        num_pages = len(self._saved_page_states)
        for state in self._saved_page_states:
            self.__dict__.update(state)
            self.draw_page_number(num_pages)
            super(NumberedCanvas, self).showPage()
        super(NumberedCanvas, self).save()

    def draw_page_number(self, page_count):
        self.saveState()
        self.setFont("Helvetica", 9)
        self.setFillColor(colors.HexColor("#718096"))
        
        # Header (pages > 1)
        if self._pageNumber > 1:
            self.drawString(54, 750, "M8 Notificaciones, Documentos y Soporte — Informe Final Soporte TP3")
            self.setStrokeColor(colors.HexColor("#E2E8F0"))
            self.setLineWidth(0.5)
            self.line(54, 742, 558, 742)
            
        # Footer
        page_text = f"Página {self._pageNumber} de {page_count}"
        self.drawRightString(558, 36, page_text)
        self.drawString(54, 36, "TPI Desarrollo de Software 2026 — Grupo 07")
        self.setStrokeColor(colors.HexColor("#E2E8F0"))
        self.setLineWidth(0.5)
        self.line(54, 48, 558, 48)
        self.restoreState()

def build_pdf(filename):
    doc = SimpleDocTemplate(
        filename,
        pagesize=letter,
        leftMargin=54,
        rightMargin=54,
        topMargin=54,
        bottomMargin=54
    )

    styles = getSampleStyleSheet()

    title_style = ParagraphStyle(
        'DocTitle',
        parent=styles['Normal'],
        fontName='Helvetica-Bold',
        fontSize=18,
        leading=22,
        textColor=colors.HexColor('#1A365D')
    )

    subtitle_style = ParagraphStyle(
        'DocSubtitle',
        parent=styles['Normal'],
        fontName='Helvetica',
        fontSize=10,
        leading=14,
        textColor=colors.HexColor('#4A5568')
    )

    h1_style = ParagraphStyle(
        'H1',
        parent=styles['Normal'],
        fontName='Helvetica-Bold',
        fontSize=12,
        leading=16,
        textColor=colors.HexColor('#2B6CB0'),
        spaceBefore=11,
        spaceAfter=5
    )

    h2_style = ParagraphStyle(
        'H2',
        parent=styles['Normal'],
        fontName='Helvetica-Bold',
        fontSize=10,
        leading=13.5,
        textColor=colors.HexColor('#2D3748'),
        spaceBefore=7,
        spaceAfter=3
    )

    body_style = ParagraphStyle(
        'Body',
        parent=styles['Normal'],
        fontName='Helvetica',
        fontSize=8.5,
        leading=12.5,
        textColor=colors.HexColor('#2D3748'),
        spaceAfter=5
    )

    bullet_style = ParagraphStyle(
        'Bullet',
        parent=styles['Normal'],
        fontName='Helvetica',
        fontSize=8.5,
        leading=12,
        textColor=colors.HexColor('#2D3748'),
        leftIndent=15,
        spaceAfter=3
    )

    table_header_style = ParagraphStyle(
        'TableHeader',
        parent=styles['Normal'],
        fontName='Helvetica-Bold',
        fontSize=8,
        leading=10.5,
        textColor=colors.white
    )

    table_cell_style = ParagraphStyle(
        'TableCell',
        parent=styles['Normal'],
        fontName='Helvetica',
        fontSize=7.8,
        leading=10,
        textColor=colors.HexColor('#2D3748')
    )

    story = []

    # Title block
    story.append(Paragraph("INFORME DE IMPLEMENTACIÓN Y TESTING: SOPORTE TP3", title_style))
    story.append(Spacer(1, 4))
    story.append(Paragraph(
        "<b>Módulo M8:</b> Notificaciones, Documentos y Soporte (Grupo 07)<br/>"
        "<b>Rama de Trabajo:</b> <code>soporte-tpi3</code><br/>"
        "<b>Fecha:</b> 24 de Noviembre de 2026 — Entrega y Cierre de Requerimientos TP2 / TP3", subtitle_style))
    story.append(Spacer(1, 6))
    story.append(HRFlowable(width="100%", thickness=1.5, color=colors.HexColor('#2B6CB0'), spaceBefore=2, spaceAfter=8))

    # Resumen Ejecutivo
    story.append(Paragraph("1. Resumen de lo Realizado en la Rama <code>soporte-tpi3</code>", h1_style))
    story.append(Paragraph(
        "En esta rama se completaron las mejoras avanzadas exigidas para la etapa de cierre e integración final (TP3) "
        "sobre el módulo de <b>Soporte (RF-8.7)</b> y la seguridad transversal de la API. Con estos cambios, Soporte "
        "deja de ser un módulo estático de base de datos y se integra de forma activa con el motor asíncrono de Notificaciones, "
        "incorporando además observabilidad operativa y protección perimetral estricta.", body_style))

    # Tabla de Cambios
    story.append(Paragraph("Detalle de Componentes Implementados", h2_style))
    cambios_data = [
        [Paragraph("Componente / Requisito", table_header_style), Paragraph("Descripción de la Implementación", table_header_style), Paragraph("Impacto y Beneficio Técnico", table_header_style)],
        [
            Paragraph("<b>Integración Célula 1 & 3</b><br/><i>Notificaciones de Tickets</i>", table_cell_style),
            Paragraph("Se conectó <code>SoporteController</code> con <code>FabricaNotificaciones</code>. Ante cada transición (<code>EN_REVISION</code>, <code>RESUELTO</code>, <code>CERRADO</code>) se genera una notificación para el titular.", table_cell_style),
            Paragraph("El cliente/conductor recibe por Email/Push aviso instantáneo de la resolución sin tener que consultar periódicamente.", table_cell_style)
        ],
        [
            Paragraph("<b>Confirmación de Recepción</b><br/><i>Alta de Ticket</i>", table_cell_style),
            Paragraph("Al crear un ticket (<code>POST /api/v1/tickets</code>), se emite automáticamente una notificación de confirmación con el ID generado.", table_cell_style),
            Paragraph("Feedback inmediato al usuario asegurando trazabilidad desde el primer segundo.", table_cell_style)
        ],
        [
            Paragraph("<b>Observabilidad y Métricas</b><br/><i>(RNF-14 / RNF-16)</i>", table_cell_style),
            Paragraph("Nuevo endpoint <code>GET /api/v1/tickets/metricas</code> exclusivo para rol <b>OPERADOR</b>. Calcula total de tickets, distribución por estado y tiempo medio de resolución.", table_cell_style),
            Paragraph("Cumple con los requisitos de diagnóstico y métricas de performance para la defensa final del TP3.", table_cell_style)
        ],
        [
            Paragraph("<b>Blindaje de Seguridad</b><br/><i>(RNF-12)</i>", table_cell_style),
            Paragraph("Se aplicó <code>[Authorize]</code> sobre <code>DocumentosController</code> (QR y Comprobantes).", table_cell_style),
            Paragraph("Cierra la brecha detectada en la revisión previa: ningún endpoint queda público sin autenticación.", table_cell_style)
        ]
    ]

    t_cambios = Table(cambios_data, colWidths=[125, 210, 169])
    t_cambios.setStyle(TableStyle([
        ('BACKGROUND', (0,0), (-1,0), colors.HexColor('#2B6CB0')),
        ('ALIGN', (0,0), (-1,-1), 'LEFT'),
        ('VALIGN', (0,0), (-1,-1), 'TOP'),
        ('INNERGRID', (0,0), (-1,-1), 0.5, colors.HexColor('#E2E8F0')),
        ('BOX', (0,0), (-1,-1), 1, colors.HexColor('#CBD5E0')),
        ('TOPPADDING', (0,0), (-1,-1), 3),
        ('BOTTOMPADDING', (0,0), (-1,-1), 3),
    ]))
    story.append(t_cambios)
    story.append(Spacer(1, 8))

    # Flujo de Notificaciones
    story.append(Paragraph("2. Flujo Completo de Notificaciones de Soporte", h1_style))
    story.append(Paragraph("1. <b>Apertura:</b> El cliente envía <code>POST /api/v1/tickets</code> con <code>Idempotency-Key</code>. Se crea el ticket en PostgreSQL y se encola una notificación: <i>'Ticket #ABC12345 recibido: Recibimos tu reclamo sobre VIAJE...'</i>.", bullet_style))
    story.append(Paragraph("2. <b>Toma del Operador:</b> El operador aplica <code>PATCH /api/v1/tickets/{id}</code> con <code>estado: EN_REVISION</code>. Se actualiza el ticket, se crea la fila en <code>CambioTicket</code> y se notifica: <i>'Tu ticket está en revisión por un operador.'</i>.", bullet_style))
    story.append(Paragraph("3. <b>Resolución:</b> El operador aplica <code>estado: RESUELTO</code> con <code>resolucion: 'Se aplicó reintegro'</code>. Se audita y se notifica: <i>'Tu reclamo fue resuelto. Resolución: Se aplicó reintegro.'</i>.", bullet_style))
    story.append(Paragraph("4. <b>Cierre:</b> El operador aplica <code>estado: CERRADO</code>. Se fija <code>cerradoEn</code> y se notifica el cierre definitivo.", bullet_style))
    story.append(Spacer(1, 8))

    # NUEVA SECCIÓN DE TESTS
    story.append(Paragraph("3. Resumen de Tests Automatizados Ejecutados (100% Passed)", h1_style))
    story.append(Paragraph(
        "Se ejecutó la suite completa de tests de integración en el entorno oficial de contenedor (Docker con SDK .NET 8), "
        "con un resultado de <b>142 tests aprobados (0 fallos)</b>. Los tests específicos de Soporte verificaron:", body_style))

    tests_data = [
        [Paragraph("Nombre del Test / Caso", table_header_style), Paragraph("Qué verifica en Soporte", table_header_style), Paragraph("Resultado", table_header_style)],
        [
            Paragraph("<code>Alta_transiciones_historial_y_repeticion_original</code>", table_cell_style),
            Paragraph("Creación de ticket con rol CLIENTE y CONDUCTOR sobre VIAJE, RESERVA y PAGO. Máquina de estados completa, bloqueo ante saltos inválidos (409 Conflict), obligatoriedad de resolución (422) y reintento idempotente idéntico.", table_cell_style),
            Paragraph("<font color='#276749'><b>PASSED</b></font><br/>(3 variantes)", table_cell_style)
        ],
        [
            Paragraph("<i>Verificación de Notificaciones Automáticas</i>", table_cell_style),
            Paragraph("Comprueba que tras las transiciones del ticket, el motor de Notificaciones haya registrado las notificaciones correspondientes asociadas al <code>destinatarioId</code>.", table_cell_style),
            Paragraph("<font color='#276749'><b>PASSED</b></font>", table_cell_style)
        ],
        [
            Paragraph("<code>Aisla_titulares_y_aplica_filtros_y_permisos</code>", table_cell_style),
            Paragraph("Seguridad RBAC: un cliente o conductor no puede ver tickets ajenos ni consultar historiales de otros usuarios (403 Forbidden). El OPERADOR puede filtrar por cualquier usuario, estado o referencia.", table_cell_style),
            Paragraph("<font color='#276749'><b>PASSED</b></font>", table_cell_style)
        ],
        [
            Paragraph("<code>Valida_clave_cuerpo_y_paginacion</code>", table_cell_style),
            Paragraph("Validaciones de contrato: rechaza cuerpos con títulos vacíos, descripciones que exceden 2000 caracteres, tipos de referencia inexistentes y parámetros de paginación inválidos (400 Bad Request).", table_cell_style),
            Paragraph("<font color='#276749'><b>PASSED</b></font>", table_cell_style)
        ],
        [
            Paragraph("<code>Operador_puede_consultar_metricas_operativas</code>", table_cell_style),
            Paragraph("Verifica el nuevo endpoint <code>GET /api/v1/tickets/metricas</code>. Valida que un CLIENTE reciba 403 Forbidden y que un OPERADOR obtenga exitosamente el resumen con tiempos medios.", table_cell_style),
            Paragraph("<font color='#276749'><b>PASSED</b></font>", table_cell_style)
        ],
        [
            Paragraph("<code>Altas_y_cambios_concurrentes_generan_un_solo_efecto</code>", table_cell_style),
            Paragraph("Prueba de estrés de concurrencia: 8 peticiones paralelas con la misma <code>Idempotency-Key</code> generan exactamente un único registro en PostgreSQL sin colisiones.", table_cell_style),
            Paragraph("<font color='#276749'><b>PASSED</b></font>", table_cell_style)
        ]
    ]

    t_tests = Table(tests_data, colWidths=[140, 305, 59])
    t_tests.setStyle(TableStyle([
        ('BACKGROUND', (0,0), (-1,0), colors.HexColor('#2B6CB0')),
        ('ALIGN', (0,0), (-1,-1), 'LEFT'),
        ('VALIGN', (0,0), (-1,-1), 'TOP'),
        ('INNERGRID', (0,0), (-1,-1), 0.5, colors.HexColor('#E2E8F0')),
        ('BOX', (0,0), (-1,-1), 1, colors.HexColor('#CBD5E0')),
        ('TOPPADDING', (0,0), (-1,-1), 3),
        ('BOTTOMPADDING', (0,0), (-1,-1), 3),
    ]))
    story.append(t_tests)
    story.append(Spacer(1, 8))

    # Qué resta en M8 para entregar todo
    story.append(Paragraph("4. Balance Final de M8: ¿Qué resta para completar todo el módulo?", h1_style))
    story.append(Paragraph("Con la rama <code>soporte-tpi3</code>, <b>Soporte queda 100% terminado</b>. Para cerrar la totalidad de M8 antes de la entrega final sólo resta:", body_style))
    story.append(Paragraph("• <b>Comprobantes PDF reales (RF-8.4):</b> Mergear <code>origin/rama_pdf</code> sobre <code>main</code> para reemplazar el mock actual por el generador QuestPDF.", bullet_style))
    story.append(Paragraph("• <b>Listener de Comprobantes:</b> Agregar una reacción en <code>IngestaEventos.cs</code> para que cuando ingrese <code>PAGO_CAPTURADO</code> se cree automáticamente la entidad <code>Comprobante</code> en base de datos.", bullet_style))

    doc.build(story, canvasmaker=NumberedCanvas)

if __name__ == '__main__':
    target_path = r"C:\Users\matia\Desktop\Facultad\Tercer Año\Desarrollo de Software\TPI\Informe_Soporte_TPI3_Grupo07.pdf"
    build_pdf(target_path)
    print(f"PDF generado exitosamente en: {target_path}")
