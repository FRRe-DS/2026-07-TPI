# TP3 - Notificaciones (M8, Grupo 7)

Lo que se agregó a la parte de **Notificaciones** de M8 para el TP3, sobre la rama `dev` (que ya incluye OpenTelemetry). Sigue a [`TP2_NOTIFICACIONES.md`](TP2_NOTIFICACIONES.md). No se modificó código de Soporte ni de Documentos.

Fecha: 26/11/2026. Rama: `notif-tp3` (sale de `dev`).

---

## 0. Qué pide el TP3 y qué le toca a Notificaciones

La consigna (`FRRe-DS/2026-TPI`, README) define el TP3 por la columna **Etapa** de los RNF. Los funcionales de Notificaciones (RF-8.1, 8.2, 8.6 y 8.8) quedaron completos en el TP2. Del TP3:

| RNF | Qué pide en TP3 | Estado en Notificaciones |
|---|---|---|
| RNF-07 Pruebas | **End-to-End** | Nuevo: `scripts/e2e-notificaciones.py` (sección 4) |
| RNF-08 Concurrencia | Nada doble ante concurrencia | Ya estaba (TP2: inbox, lease, concurrencia optimista) |
| RNF-12 Seguridad | HTTPS en entornos publicados y **controles básicos de vulnerabilidades web** | Nuevo: encabezados de seguridad y HSTS (sección 2) |
| RNF-13 Resiliencia | Timeouts, reintentos limitados | Ya estaba (TP2) |
| RNF-14 Salud y diagnóstico | Correlación por solicitud, **viaje o reserva** | Ampliado: los logs llevan `ViajeId`/`ReservaId`/`PagoId` (sección 3) |
| RNF-16 Performance | Una meta medida del recorrido crítico | Métricas de OpenTelemetry (DevOps, rama `dev`) |
| RNF-19 Privacidad y auditoría | Nada sensible en logs/URLs; **las operaciones críticas conservan trazabilidad** | Nuevo: tabla de auditoría y emisor de cada evento (sección 1) |
| RNF-20 Documentación | Docs junto a la versión | Este archivo |

Además, Notificaciones le ofrece a Documentos el **reenvío de comprobantes** (RF-8.5) para que no tenga que armar la notificación por su cuenta (sección 5).

## 1. Trazabilidad de operaciones críticas (RNF-19)

Hasta el TP2, quién mandaba un aviso manual o reintentaba uno solo quedaba en el log de consola. Ahora queda en la base:

- **Tabla `NotificacionAuditoria`** (entidad `AuditoriaNotificacion`): `NotificacionId`, `Accion` (`ALTA_MANUAL`, `REINTENTO_MANUAL`, `REENVIO`), `Actor` (el `azp`/`client_id`/`sub` del token, nunca un email), `RequestId` (el `X-Request-Id`, para cruzarlo con los logs) y `RealizadaEn`. Solo se insertan filas, nunca se modifican.
  - Se escribe en el **mismo `SaveChanges`** que la operación: si el reintento pierde la carrera de concurrencia (409), tampoco queda auditado.
- **`EventosRecibidos.EmitidoPor`**: quién informó cada evento. Por HTTP, el client del token (por ejemplo `grupo-05`); por RabbitMQ, el `app_id` del mensaje. No entra en el hash de idempotencia: el mismo evento por los dos caminos sigue siendo un duplicado.
- Migración `20261126165906_NotificacionesTp3Auditoria`: solo agrega la tabla y la columna (no toca datos existentes).

## 2. Controles básicos de vulnerabilidades web (RNF-12)

`Infrastructure/Http/EncabezadosSeguridadMiddleware.cs`, en el pipeline común de M8 (el que armamos en el TP2). En **toda** respuesta, incluidos los errores:

| Encabezado | Para qué |
|---|---|
| `X-Content-Type-Options: nosniff` | El navegador no interpreta un JSON como HTML o script |
| `X-Frame-Options: DENY` y `frame-ancestors 'none'` | Nadie puede embeber M8 (clickjacking) |
| `Referrer-Policy: no-referrer` | Las URLs de M8 no se filtran a otros sitios |
| `Content-Security-Policy: default-src 'none'` | Una API no tiene nada que cargar ni ejecutar. Se omite en Development para que funcione Swagger UI |
| `Cache-Control: no-store` (solo `/api`) | Avisos, tickets y comprobantes no quedan en cachés intermedias |

Fuera de Development se agrega **HSTS** (`UseHsts`). En un entorno publicado, el HTTPS lo termina el proxy o ingress, y la API ya redirige a HTTPS cuando conoce el puerto.

## 3. Correlación por viaje, reserva o pago (RNF-14)

`Features/Notificaciones/ScopeLog.cs`. Además del `RequestId` del TP2:

- La **ingesta** abre un scope con `EventoId`, `ViajeId`, `ReservaId` y `PagoId` (los que traiga el evento).
- Cada **intento de envío** abre un scope con `NotificacionId`, `EventoId` y la referencia del aviso como `ViajeId`, `ReservaId` o `PagoId`.

Con los logs en JSON (como en `docker-compose.yml`) se puede filtrar todo lo que pasó con un viaje, desde la llegada del evento hasta el último intento. Los scopes solo llevan identificadores, nunca contactos ni el texto de los avisos.

## 4. Prueba End-to-End (RNF-07)

`scripts/e2e-notificaciones.py` recorre el flujo completo contra el entorno real (API, PostgreSQL, RabbitMQ, Redis, Mailpit y el Keycloak de la cátedra). Solo usa la biblioteca estándar de Python y sale con código 0 si todo pasa:

1. `/health` 200 con los encabezados de seguridad; sin token → 401 con `X-Request-Id`.
2. `VIAJE_ASIGNADO` por HTTP → 202; repetido → misma respuesta; mismo `eventoId` con otro cuerpo → 422.
3. `RESERVA_CONFIRMADA` publicado en `movilidad.eventos` (RabbitMQ) → consumido.
4. Los tres avisos quedan `ENVIADA` (cliente por EMAIL, conductor por SMS) y los dos correos llegan a Mailpit.
5. Un contacto `@falla.test` hace 3 intentos y queda `FALLIDA`; el reintento del OPERADOR agrega **exactamente un** intento (historial de 4); la misma `Idempotency-Key` devuelve la respuesta original; reintentar una `ENVIADA` → 409.
6. Ninguna respuesta de la API contiene los emails de los destinatarios.

Cómo correrlo:

```powershell
docker compose up -d --build        # M8 (puerto 8088), RabbitMQ (15672), Mailpit (8025)
# Keycloak de la cátedra en el puerto 8080 (repo 2026-TPI, carpeta keycloak)
$env:KEYCLOAK_CLIENT_SECRET = "<secreto del client grupo-07>"
$env:RABBITMQ_USER = "<el del .env>"; $env:RABBITMQ_PASSWORD = "<el del .env>"
python scripts/e2e-notificaciones.py
```

Con `M8_TEST_API`, `M8_TEST_MAILPIT` y `M8_TEST_RABBITMQ_API` se cambian las URLs (solo acepta `localhost`). DevOps lo puede usar como la prueba E2E del PR `dev → main` (issue #5).

Resultado real (26/11/2026): todos los pasos `ok` contra un stack aislado con la imagen de esta rama. En la base quedaron las filas de auditoría (`REINTENTO_MANUAL`, actor `grupo-07`) y los emisores (`grupo-07` por HTTP y `e2e-m9` por RabbitMQ). En los logs JSON, 13 líneas llevaban `ViajeId` y 2 `ReservaId`, y no apareció **ningún** email.

## 5. Reenvío de comprobantes, para Documentos (RF-8.5)

`Features/Notificaciones/Servicios/ReenviosNotificaciones.cs` (registrado en DI). El contrato dice que `POST /api/v1/comprobantes/{id}/reenvios` "envía el enlace al contacto registrado del cliente, nunca a una dirección indicada en la solicitud" y que "genera una notificación que sigue la misma política de intentos". Eso es Notificaciones, así que se lo ofrecemos armado:

```csharp
// En el controller de Documentos, después de validar el comprobante (existe, es del cliente, GENERADO)
// y dentro de EjecucionIdempotente (Idempotency-Key):
var n = await _reenvios.RegistrarAsync(comprobante.ClienteId, solicitud.Canal, "Tu comprobante",
    $"Tu comprobante está disponible en {comprobante.UrlPdf}.", TipoReferencia.PAGO, comprobante.PagoId,
    User.Actor(), HttpContext.TraceIdentifier, ct);
return StatusCode(202, NotificacionResponse.De(n));   // esquema Notificacion del contrato
```

Usa el contacto registrado (aprendido de los eventos), crea la notificación con `Origen = REENVIO` (la envía el despachador, con 3 intentos) y la audita. Si el cliente no tiene contacto para ese canal, nace `FALLIDA`, como dice el contrato. Hoy el endpoint de Documentos es un stub que recibe un email: queda de su lado conectarlo.

## 6. Pruebas

- `tests/.../Notificaciones/Tp3NotificacionesTests.cs`: 10 casos nuevos (auditoría con actor y `RequestId`, el 409 no audita, emisor por HTTP, encabezados en `/api` y `/health`, scopes de log sin contacto, reenvío al contacto registrado y enviado, reenvío sin contacto → `FALLIDA`).
- `ApiDePrueba` ignora `ManyServiceProvidersCreatedWarning` de EF. Pasadas las 20 instancias de la API en un mismo proceso de tests, EF lo trataba como error y la API respondía 500. Con la suite creciendo (Soporte, Documentos) se iba a dar.
- Resultado: **168 de 168, 0 omitidas** con PostgreSQL, RabbitMQ y Redis reales (variables `M8_TEST_*`); 162 + 6 omitidas sin ellos.
- Aviso para DevOps: `Observability/PrivacidadTests.La_exportacion_omite_...` falla en alrededor de 1 de cada 2 corridas, **también en `dev` sin estos cambios**. A veces verifica antes de que se exporte el span HTTP del servidor. No es de Notificaciones.

## Índice de archivos

**Nuevos:** `Features/Notificaciones/Entities/AuditoriaNotificacion.cs`, `Features/Notificaciones/ScopeLog.cs`, `Features/Notificaciones/Servicios/ReenviosNotificaciones.cs`, `Infrastructure/Http/EncabezadosSeguridadMiddleware.cs`, migración `20261126165906_NotificacionesTp3Auditoria`, `tests/.../Notificaciones/Tp3NotificacionesTests.cs`, `scripts/e2e-notificaciones.py`, este archivo.

**Modificados:** `NotificacionesController.cs` (auditoría), `NotificacionesModelo.cs`, `M8DbContext.cs` (un `DbSet`), `EventoRecibido.cs`, `IngestaEventos.cs` (emisor y scope), `EventosController.cs` y `ConsumidorEventosRabbitMq.cs` (pasan el emisor), `ServicioEnvio.cs` (scope), `FabricaNotificaciones.cs` (`Reenvio`, al final de la clase), `NotificacionesServiceCollectionExtensions.cs`, `Program.cs` (middleware y HSTS), `ApiDePrueba.cs`.

**Compatibilidad con el PR #7 de Soporte:** mergeamos los dos en una copia descartable y entran sin conflictos (`FabricaNotificaciones` y `Enums.cs` se tocan en zonas distintas).
