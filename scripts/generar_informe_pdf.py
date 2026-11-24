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
            self.drawString(54, 750, "M8 Notificaciones, Documentos y Soporte — Informe de Integración y Contratos")
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
        fontSize=20,
        leading=24,
        textColor=colors.HexColor('#1A365D')
    )

    subtitle_style = ParagraphStyle(
        'DocSubtitle',
        parent=styles['Normal'],
        fontName='Helvetica',
        fontSize=11,
        leading=15,
        textColor=colors.HexColor('#4A5568')
    )

    h1_style = ParagraphStyle(
        'H1',
        parent=styles['Normal'],
        fontName='Helvetica-Bold',
        fontSize=14,
        leading=18,
        textColor=colors.HexColor('#2B6CB0'),
        spaceBefore=14,
        spaceAfter=6
    )

    h2_style = ParagraphStyle(
        'H2',
        parent=styles['Normal'],
        fontName='Helvetica-Bold',
        fontSize=11,
        leading=15,
        textColor=colors.HexColor('#2D3748'),
        spaceBefore=10,
        spaceAfter=4
    )

    body_style = ParagraphStyle(
        'Body',
        parent=styles['Normal'],
        fontName='Helvetica',
        fontSize=9.5,
        leading=13.5,
        textColor=colors.HexColor('#2D3748'),
        spaceAfter=6
    )

    bullet_style = ParagraphStyle(
        'Bullet',
        parent=styles['Normal'],
        fontName='Helvetica',
        fontSize=9,
        leading=13,
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

    alert_box_style = ParagraphStyle(
        'AlertBox',
        parent=styles['Normal'],
        fontName='Helvetica',
        fontSize=8.5,
        leading=12,
        textColor=colors.HexColor('#742A2A')
    )

    story = []

    # Title block
    story.append(Paragraph("INFORME DE COMPATIBILIDAD E INTEGRACIÓN DE CONTRATOS", title_style))
    story.append(Spacer(1, 4))
    story.append(Paragraph("<b>Módulo Asignado:</b> M8 — Notificaciones, Documentos y Soporte (Grupo 07)<br/><b>Fecha de Emisión:</b> 23 de Noviembre de 2026<br/><b>Destinatarios:</b> Equipo de Trabajo Grupo 07 y Consorcio de Integración", subtitle_style))
    story.append(Spacer(1, 10))
    story.append(HRFlowable(width="100%", thickness=1.5, color=colors.HexColor('#2B6CB0'), spaceBefore=2, spaceAfter=12))

    # Resumen Ejecutivo
    story.append(Paragraph("1. Resumen Ejecutivo y Alcance", h1_style))
    story.append(Paragraph(
        "El presente documento analiza la compatibilidad técnica entre la implementación oficial de <b>M8</b> "
        "y los contratos OpenAPI v3 entregados por los módulos con los que interactúa directamente en el TPI: "
        "<b>M6 (Viajes y Ciclo de Vida)</b>, <b>M7 (Tarifas y Pagos)</b>, <b>M9 (Reservas)</b> y la situación de <b>M5 (Solicitudes de Viajes)</b>. "
        "El objetivo es identificar divergencias de esquemas y canales para coordinar con los otros grupos "
        "antes de las pruebas de integración en vivo.", body_style))

    # Matriz Global
    story.append(Paragraph("Matriz de Estado Global de Interacción", h2_style))
    
    matriz_data = [
        [Paragraph("Módulo / Contraparte", table_header_style), Paragraph("Vía de Comunicación", table_header_style), Paragraph("Estado de Compatibilidad", table_header_style), Paragraph("Acción Prioritaria Requerida", table_header_style)],
        [Paragraph("<b>M6</b> — Viajes (G5/G12)<br/><i>Validación QR (RF-8.3)</i>", table_cell_style), Paragraph("HTTP REST (Sincrónico)", table_cell_style), Paragraph("<font color='#276749'><b>100% COMPATIBLE</b></font>", table_cell_style), Paragraph("Ninguna. Endpoints y DTOs coinciden exactamente.", table_cell_style)],
        [Paragraph("<b>M6</b> — Viajes (G5/G12)<br/><i>Eventos de Ciclo de Vida</i>", table_cell_style), Paragraph("RabbitMQ (Asincrónico)", table_cell_style), Paragraph("<font color='#C53030'><b>INCOMPATIBLE CRÍTICO</b></font>", table_cell_style), Paragraph("Divergencia de nombres (inglés vs español) y sobre envelope.", table_cell_style)],
        [Paragraph("<b>M7</b> — Pagos (G6/G11)<br/><i>Cobros y Reembolsos</i>", table_cell_style), Paragraph("RabbitMQ (Asincrónico)", table_cell_style), Paragraph("<font color='#D69E2E'><b>COMPATIBLE CON ACUERDO</b></font>", table_cell_style), Paragraph("Acordar casing (camelCase vs snake_case).", table_cell_style)],
        [Paragraph("<b>M9</b> — Reservas (G9)<br/><i>Avisos de Reserva (RF-8.2)</i>", table_cell_style), Paragraph("RabbitMQ (Asincrónico)", table_cell_style), Paragraph("<font color='#276749'><b>100% COMPATIBLE</b></font>", table_cell_style), Paragraph("Totalmente alineado en enums, eventos y DTOs.", table_cell_style)],
        [Paragraph("<b>M5</b> — Oferta/Despacho<br/><i>Asignaciones de Viaje</i>", table_cell_style), Paragraph("RabbitMQ / HTTP", table_cell_style), Paragraph("<font color='#718096'><b>SIN CONTRATO FORMAL</b></font>", table_cell_style), Paragraph("Solicitar especificación OpenAPI o fijar Catálogo M8.", table_cell_style)]
    ]

    t_matriz = Table(matriz_data, colWidths=[130, 95, 115, 164])
    t_matriz.setStyle(TableStyle([
        ('BACKGROUND', (0,0), (-1,0), colors.HexColor('#2B6CB0')),
        ('ALIGN', (0,0), (-1,-1), 'LEFT'),
        ('VALIGN', (0,0), (-1,-1), 'TOP'),
        ('INNERGRID', (0,0), (-1,-1), 0.5, colors.HexColor('#E2E8F0')),
        ('BOX', (0,0), (-1,-1), 1, colors.HexColor('#CBD5E0')),
        ('TOPPADDING', (0,0), (-1,-1), 4),
        ('BOTTOMPADDING', (0,0), (-1,-1), 4),
    ]))
    story.append(t_matriz)
    story.append(Spacer(1, 10))

    # M6 Detallado
    story.append(Paragraph("2. M6 — Viajes y Ciclo de Vida (Grupos 5 y 12)", h1_style))
    story.append(Paragraph("<b>A. Lo que es 100% Compatible: Consumo y Validación de QR (RF-8.3)</b>", h2_style))
    story.append(Paragraph(
        "En su endpoint <code>POST /trips/{tripId}/start</code>, el conductor envía el token del QR (<code>verificationToken</code>). "
        "M6 consume y valida contra la API de M8 en <code>POST /api/v1/qr/validaciones</code> enviando <code>{ token, viajeId }</code>. "
        "La implementación de M8 responde <code>200 OK</code> con <code>{ valido, viajeId, usadoEn }</code>, o los errores de contrato "
        "<code>404</code> (inexistente), <code>409</code> (ya usado o reemplazado) y <code>422</code> (expirado/otro viaje). <b>Coincidencia total.</b>", body_style))

    story.append(Paragraph("<b>B. Incompatibilidad Crítica: Discrepancia en Eventos RabbitMQ</b>", h2_style))
    story.append(Paragraph(
        "El contrato de M6 publica eventos bajo su propio esquema <code>EventEnvelope</code> con nombres en inglés y encapsulamiento propio. "
        "Si M6 publica tal como está su archivo OpenAPI, el consumidor de M8 rechazará los mensajes como 'mensajes venenosos' enviándolos a la Dead Letter Queue (DLQ).", body_style))

    m6_diff_data = [
        [Paragraph("Concepto", table_header_style), Paragraph("Contrato M6 (Grupos 5 y 12)", table_header_style), Paragraph("Esquema Oficial M8 (EventoEntrada)", table_header_style)],
        [Paragraph("ID del evento", table_cell_style), Paragraph("<code>eventId</code> (UUID)", table_cell_style), Paragraph("<code>eventoId</code> (UUID obligatorio)", table_cell_style)],
        [Paragraph("Identificador del Viaje", table_cell_style), Paragraph("<code>tripId</code>", table_cell_style), Paragraph("<code>viajeId</code>", table_cell_style)],
        [Paragraph("Tipo de Evento", table_cell_style), Paragraph("<code>eventType</code>: <code>DriverArrived</code>, <code>TripStarted</code>, <code>TripCompleted</code>, <code>TripCancelled</code>", table_cell_style), Paragraph("<code>tipo</code>: <code>VIAJE_ARRIBO_CONDUCTOR</code>, <code>VIAJE_INICIADO</code>, <code>VIAJE_FINALIZADO</code>, <code>VIAJE_CANCELADO</code>", table_cell_style)],
        [Paragraph("Identificación de Origen", table_cell_style), Paragraph("<i>Ausente en el sobre</i>", table_cell_style), Paragraph("<code>origen: \"M6\"</code> (Obligatorio en M8)", table_cell_style)],
        [Paragraph("Estructura de Datos", table_cell_style), Paragraph("Envuelto en <code>payload: { ... }</code>", table_cell_style), Paragraph("Esquema estándar con <code>destinatarios</code> y <code>detalle</code>", table_cell_style)]
    ]
    t_m6 = Table(m6_diff_data, colWidths=[110, 195, 199])
    t_m6.setStyle(TableStyle([
        ('BACKGROUND', (0,0), (-1,0), colors.HexColor('#C53030')),
        ('ALIGN', (0,0), (-1,-1), 'LEFT'),
        ('VALIGN', (0,0), (-1,-1), 'TOP'),
        ('INNERGRID', (0,0), (-1,-1), 0.5, colors.HexColor('#E2E8F0')),
        ('BOX', (0,0), (-1,-1), 1, colors.HexColor('#FEB2B2')),
        ('TOPPADDING', (0,0), (-1,-1), 4),
        ('BOTTOMPADDING', (0,0), (-1,-1), 4),
    ]))
    story.append(t_m6)
    story.append(Spacer(1, 10))

    # M7 Detallado
    story.append(Paragraph("3. M7 — Tarifas, Pagos y Liquidaciones (Grupos 6 y 11)", h1_style))
    story.append(Paragraph(
        "<b>Situación:</b> M7 declara en su contrato OpenAPI que la mensajería asíncrona no fue fijada con un esquema rígido interno, "
        "sino que adopta explícitamente el <b>Catálogo Común de Eventos de la Cátedra</b>. Esto representa una ventaja para M8.", body_style))
    story.append(Paragraph("<b>Puntos Clave de Integración:</b>", h2_style))
    story.append(Paragraph("• <b>Eventos que M8 consume de M7:</b> <code>PAGO_CAPTURADO</code> (pago aprobado) y <code>REINTEGRO_REGISTRADO</code> (reembolso). Con el primero se notifica al cliente y se emite el comprobante PDF (RF-8.4); con el segundo se emite el comprobante de reintegro.", bullet_style))
    story.append(Paragraph("• <b>Atención al Casing (snake_case vs camelCase):</b> M7 utiliza convención <code>snake_case</code> en su API (<code>viaje_id</code>, <code>pago_id</code>, <code>cliente_id</code>). En RabbitMQ deben acordar emitir con los nombres acordados en el catálogo común de M8 (<code>viajeId</code>, <code>pagoId</code>, <code>eventoId</code>).", bullet_style))
    story.append(Paragraph("• <b>Módulo de Soporte (RF-8.7):</b> M8 ya cuenta con soporte nativo para vincular reclamos de pagos: <code>referencia.tipo = \"PAGO\"</code> y <code>referencia.id = pago_id</code>.", bullet_style))
    story.append(Spacer(1, 10))

    # M9 Detallado
    story.append(Paragraph("4. M9 — Reservas de Viajes (Grupo 9)", h1_style))
    story.append(Paragraph(
        "<b>Estado: 100% COMPATIBLE Y HOMOLOGADO.</b> La coordinación entre M9 y M8 es perfecta tanto en el ciclo de vida como en los tipos de datos:", body_style))
    story.append(Paragraph("• <b>Catálogo de Eventos M8 (RF-8.2):</b> M8 implementó plantillas de notificación exactas para todo el ciclo de M9: <code>RESERVA_CREADA</code>, <code>RESERVA_CONFIRMADA</code>, <code>RESERVA_MODIFICADA</code>, <code>RESERVA_RECORDATORIO</code>, <code>RESERVA_ACTIVADA</code>, <code>RESERVA_CANCELADA</code> y <code>RESERVA_VENCIDA</code>.", bullet_style))
    story.append(Paragraph("• <b>Identificadores y Tipos:</b> M9 utiliza <code>reservaId</code> (UUID), tipo de vehículo en mayúsculas (<code>AUTO</code>, <code>MOTO</code>), roles (<code>CLIENTE</code>), fechas en ISO 8601 UTC y zonas horarias en formato IANA (ej. <code>America/Argentina/Buenos_Aires</code>), todas soportadas de forma estricta por el validador de M8.", bullet_style))
    story.append(Paragraph("• <b>Soporte (RF-8.7):</b> La API de tickets de M8 admite <code>referencia.tipo = \"RESERVA\"</code> para reclamos directos sobre reservas.", bullet_style))
    story.append(Spacer(1, 10))

    # M5 Detallado
    story.append(Paragraph("5. M5 — Oferta y Asignación de Solicitudes", h1_style))
    story.append(Paragraph(
        "<b>Situación Actual:</b> M5 <b>no ha provisto contrato OpenAPI público</b> en el repositorio central hasta la fecha.", body_style))
    story.append(Paragraph("<b>Impacto sobre M8:</b>", h2_style))
    story.append(Paragraph("• M8 necesita conocer los eventos <code>VIAJE_SOLICITADO</code> y <code>VIAJE_ASIGNADO</code> (con <code>conductorId</code>) para la trazabilidad y la reconstrucción del historial mediante Event Sourcing / CQRS.", bullet_style))
    story.append(Paragraph("• <b>Postura formal de M8:</b> Como M8 ya tiene implementado y testeado su endpoint de ingesta y su consumidor RabbitMQ bajo la especificación del catálogo común, se le debe exigir formalmente a M5 que publique sus eventos bajo el formato <code>EventoEntrada</code> de M8.", bullet_style))
    story.append(Spacer(1, 10))

    # Acciones a Seguir
    story.append(Paragraph("6. Plan de Acción y Propuestas para el Consorcio", h1_style))
    story.append(Paragraph("Para destrabar las diferencias y garantizar que todo el sistema integrado funcione sin fallos, el Grupo 07 propone los siguientes pasos concretos:", body_style))
    
    story.append(Paragraph("<b>1. Notificar al Grupo 5 y Grupo 12 (M6):</b><br/>"
                           "Informar que para la mensajería asíncrona no pueden emitir con <code>EventEnvelope</code> en inglés (<code>eventId</code>, <code>tripId</code>). Deben publicar en el exchange <code>movilidad.eventos</code> utilizando el sobre estándar del catálogo común de la cátedra con <code>eventoId</code>, <code>tipo</code> (en mayúsculas) y <code>viajeId</code>. "
                           "<i>(Alternativa de contingencia en M8: si M6 se niega a cambiarlo, podemos implementar un middleware deserializador adaptador en M8 que mapee ambos esquemas).</i>", bullet_style))
    
    story.append(Paragraph("<b>2. Coordinar con Grupo 6 y Grupo 11 (M7):</b><br/>"
                           "Acordar que los eventos de cobro emitidos por M7 viajen con la routing key <code>pago.capturado</code> y respeten el casing <code>viajeId</code> / <code>pagoId</code>.", bullet_style))

    story.append(Paragraph("<b>3. Emplazar al Grupo responsable de M5:</b><br/>"
                           "Enviarles la especificación JSON del esquema <code>EventoEntrada</code> de M8 para que su cola de asignación emita con ese contrato.", bullet_style))

    doc.build(story, canvasmaker=NumberedCanvas)

if __name__ == '__main__':
    target_path = r"C:\Users\matia\Desktop\Facultad\Tercer Año\Desarrollo de Software\TPI\Informe_Compatibilidad_Contratos_M8.pdf"
    build_pdf(target_path)
    print(f"PDF generado exitosamente en: {target_path}")
