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
        fontSize=19,
        leading=23,
        textColor=colors.HexColor('#1A365D')
    )

    subtitle_style = ParagraphStyle(
        'DocSubtitle',
        parent=styles['Normal'],
        fontName='Helvetica',
        fontSize=10.5,
        leading=14.5,
        textColor=colors.HexColor('#4A5568')
    )

    h1_style = ParagraphStyle(
        'H1',
        parent=styles['Normal'],
        fontName='Helvetica-Bold',
        fontSize=13,
        leading=17,
        textColor=colors.HexColor('#2B6CB0'),
        spaceBefore=12,
        spaceAfter=5
    )

    h2_style = ParagraphStyle(
        'H2',
        parent=styles['Normal'],
        fontName='Helvetica-Bold',
        fontSize=10.5,
        leading=14,
        textColor=colors.HexColor('#2D3748'),
        spaceBefore=8,
        spaceAfter=4
    )

    body_style = ParagraphStyle(
        'Body',
        parent=styles['Normal'],
        fontName='Helvetica',
        fontSize=9,
        leading=13,
        textColor=colors.HexColor('#2D3748'),
        spaceAfter=5
    )

    bullet_style = ParagraphStyle(
        'Bullet',
        parent=styles['Normal'],
        fontName='Helvetica',
        fontSize=8.8,
        leading=12.5,
        textColor=colors.HexColor('#2D3748'),
        leftIndent=15,
        spaceAfter=3
    )

    table_header_style = ParagraphStyle(
        'TableHeader',
        parent=styles['Normal'],
        fontName='Helvetica-Bold',
        fontSize=8.5,
        leading=11,
        textColor=colors.white
    )

    table_cell_style = ParagraphStyle(
        'TableCell',
        parent=styles['Normal'],
        fontName='Helvetica',
        fontSize=8,
        leading=10.5,
        textColor=colors.HexColor('#2D3748')
    )

    code_style = ParagraphStyle(
        'CodeText',
        parent=styles['Normal'],
        fontName='Courier',
        fontSize=8,
        leading=10,
        textColor=colors.HexColor('#2C5282')
    )

    story = []

    # Title block
    story.append(Paragraph("INFORME DE IMPLEMENTACIÓN: SOPORTE Y TICKETS TP3", title_style))
    story.append(Spacer(1, 4))
    story.append(Paragraph(
        "<b>Módulo M8:</b> Notificaciones, Documentos y Soporte (Grupo 07)<br/>"
        "<b>Rama de Trabajo:</b> <code>soporte-tpi3</code><br/>"
        "<b>Fecha:</b> 24 de Noviembre de 2026 — Entrega y Cierre de Requerimientos TP2 / TP3", subtitle_style))
    story.append(Spacer(1, 8))
    story.append(HRFlowable(width="100%", thickness=1.5, color=colors.HexColor('#2B6CB0'), spaceBefore=2, spaceAfter=10))

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
        ],
        [
            Paragraph("<b>Suite de Tests TP3</b><br/><i>(RNF-07)</i>", table_cell_style),
            Paragraph("Se actualizaron los tests en <code>SoporteControllerTests.cs</code> verificando generación de notificaciones y permisos de métricas.", table_cell_style),
            Paragraph("Cobertura de pruebas automatizadas contra regresiones en CI/CD.", table_cell_style)
        ]
    ]

    t_cambios = Table(cambios_data, colWidths=[130, 204, 170])
    t_cambios.setStyle(TableStyle([
        ('BACKGROUND', (0,0), (-1,0), colors.HexColor('#2B6CB0')),
        ('ALIGN', (0,0), (-1,-1), 'LEFT'),
        ('VALIGN', (0,0), (-1,-1), 'TOP'),
        ('INNERGRID', (0,0), (-1,-1), 0.5, colors.HexColor('#E2E8F0')),
        ('BOX', (0,0), (-1,-1), 1, colors.HexColor('#CBD5E0')),
        ('TOPPADDING', (0,0), (-1,-1), 4),
        ('BOTTOMPADDING', (0,0), (-1,-1), 4),
    ]))
    story.append(t_cambios)
    story.append(Spacer(1, 10))

    # Flujo de Notificaciones
    story.append(Paragraph("2. Flujo Completo de Notificaciones de Soporte", h1_style))
    story.append(Paragraph(
        "El ciclo de vida del ticket y sus notificaciones automáticas quedó orquestado de la siguiente manera:", body_style))
    story.append(Paragraph("1. <b>Apertura:</b> El cliente envía <code>POST /api/v1/tickets</code> con <code>Idempotency-Key</code>. Se crea el ticket en PostgreSQL y se encola una notificación: <i>'Ticket #ABC12345 recibido: Recibimos tu reclamo sobre VIAJE...'</i>.", bullet_style))
    story.append(Paragraph("2. <b>Toma del Operador:</b> El operador aplica <code>PATCH /api/v1/tickets/{id}</code> con <code>estado: EN_REVISION</code>. Se actualiza el ticket, se crea la fila en <code>CambioTicket</code> y se notifica: <i>'Tu ticket está en revisión por un operador.'</i>.", bullet_style))
    story.append(Paragraph("3. <b>Resolución:</b> El operador aplica <code>estado: RESUELTO</code> con <code>resolucion: 'Se aplicó reintegro'</code>. Se audita y se notifica: <i>'Tu reclamo fue resuelto. Resolución: Se aplicó reintegro.'</i>.", bullet_style))
    story.append(Paragraph("4. <b>Cierre:</b> El operador aplica <code>estado: CERRADO</code>. Se fija <code>cerradoEn</code> y se notifica el cierre definitivo.", bullet_style))
    story.append(Spacer(1, 10))

    # Métricas Operativas
    story.append(Paragraph("3. Endpoint de Métricas Operativas (<code>GET /api/v1/tickets/metricas</code>)", h1_style))
    story.append(Paragraph(
        "Permite al operador o administrador consultar el rendimiento de la mesa de soporte en tiempo real:", body_style))
    story.append(Paragraph("<b>Payload de Respuesta (200 OK):</b>", h2_style))
    ejemplo_json = (
        "{\n"
        "  \"totalTickets\": 42,\n"
        "  \"abiertos\": 5,\n"
        "  \"enRevision\": 8,\n"
        "  \"resueltos\": 25,\n"
        "  \"cerrados\": 4,\n"
        "  \"tiempoMedioResolucionMinutos\": 18.5\n"
        "}"
    )
    story.append(Paragraph(f"<font face='Courier' color='#2C5282'>{ejemplo_json.replace(chr(10), '<br/>&nbsp;&nbsp;')}</font>", body_style))
    story.append(Spacer(1, 10))

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
