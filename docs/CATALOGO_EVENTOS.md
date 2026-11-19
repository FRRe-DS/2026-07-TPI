# Catálogo de eventos que consume M8 (RNF-03, RNF-10)

M8 recibe los hitos de M5, M6, M7 y M9 **por dos caminos con el mismo formato** (esquema `EventoEntrada` del contrato, `docs/m8-openapi.yaml`):

1. **HTTP:** `POST /api/v1/eventos` con un JWT de rol `SERVICIO` → `202`.
2. **RabbitMQ:** publicando el mismo JSON en el exchange común.

> Esta es la propuesta de M8 para el consorcio. Los nombres de exchange y colas son configurables (sección `RabbitMQ` de `appsettings.json`), así que si el consorcio acuerda otros, se cambian sin tocar código.

## Topología

| Elemento | Nombre | Tipo | Quién lo declara |
|---|---|---|---|
| Exchange común de hitos | `movilidad.eventos` | `topic`, durable | Cualquiera (declaración idempotente). M8 lo declara al arrancar. |
| Cola de M8 | `m8.eventos` | `quorum`, durable, `x-delivery-limit = 5` | M8 |
| Bindings de M8 | `viaje.#`, `reserva.#`, `pago.#`, `reintegro.#` | — | M8 |
| Exchange de errores | `m8.eventos.dlx` | `fanout`, durable | M8 |
| Cola de errores (DLQ) | `m8.eventos.dlq` | durable | M8 |

## Cómo publicar un evento

- **Cuerpo:** JSON UTF-8 con la forma de `EventoEntrada` (el mismo de `POST /api/v1/eventos`).
- **Propiedades:** `content_type = application/json`, `delivery_mode = 2` (persistente). `correlation_id` es opcional: si cumple el formato de `X-Request-Id`, M8 lo usa para correlacionar sus logs.
- **Routing key:** el `tipo` en minúsculas, con cada `_` reemplazado por `.` (lo calcula `ConsumidorEventosRabbitMq.ClaveDe`):

| `tipo` | Emite | Routing key | Identificadores obligatorios |
|---|---|---|---|
| `VIAJE_SOLICITADO` | M5 | `viaje.solicitado` | `viajeId` |
| `VIAJE_ASIGNADO` | M5 | `viaje.asignado` | `viajeId` |
| `VIAJE_ARRIBO_CONDUCTOR` | M6 | `viaje.arribo.conductor` | `viajeId` |
| `VIAJE_INICIADO` | M6 | `viaje.iniciado` | `viajeId` |
| `VIAJE_CANCELADO` | M6 | `viaje.cancelado` | `viajeId` |
| `VIAJE_FINALIZADO` | M6 | `viaje.finalizado` | `viajeId` |
| `RESERVA_CREADA` | M9 | `reserva.creada` | `reservaId` |
| `RESERVA_MODIFICADA` | M9 | `reserva.modificada` | `reservaId` |
| `RESERVA_CONFIRMADA` | M9 | `reserva.confirmada` | `reservaId` |
| `RESERVA_RECORDATORIO` | M9 | `reserva.recordatorio` | `reservaId` |
| `RESERVA_ACTIVADA` | M9 | `reserva.activada` | `reservaId` |
| `RESERVA_CANCELADA` | M9 | `reserva.cancelada` | `reservaId` |
| `RESERVA_VENCIDA` | M9 | `reserva.vencida` | `reservaId` |
| `PAGO_CAPTURADO` | M7 | `pago.capturado` | `pagoId` y `viajeId` |
| `REINTEGRO_REGISTRADO` | M7 | `reintegro.registrado` | `pagoId` y `viajeId` |

(M8 no depende de la routing key para decidir nada: lee el `tipo` del cuerpo. La clave solo sirve para enrutar.)

Ejemplo:

```json
{
  "eventoId": "7b1f2c3e-1111-4c4c-9a9a-0123456789ab",
  "tipo": "VIAJE_ASIGNADO",
  "origen": "M5",
  "ocurridoEn": "2026-11-18T15:00:00Z",
  "viajeId": "5f2a0000-2222-4c4c-9a9a-0123456789ab",
  "destinatarios": [
    { "usuarioId": "c1000000-3333-4c4c-9a9a-0123456789ab", "rol": "CLIENTE", "email": "cliente@ejemplo.com", "canalesPreferidos": ["EMAIL"] },
    { "usuarioId": "d2000000-4444-4c4c-9a9a-0123456789ab", "rol": "CONDUCTOR", "telefono": "+5493624000000" }
  ],
  "detalle": { "origen": "Av. Sarmiento 100", "destino": "Plaza 25 de Mayo", "tipoVehiculo": "AUTO" }
}
```

## Garantías que da M8

| Situación | Qué hace M8 |
|---|---|
| Mismo `eventoId` y mismo cuerpo (reintento, reentrega de RabbitMQ, o llegó por HTTP **y** por la cola) | Lo registra una sola vez y no duplica avisos. Por HTTP devuelve la respuesta original. En la cola, `ack`. |
| Mismo `eventoId` con otro cuerpo | HTTP `422`. En la cola va a la DLQ. |
| JSON inválido o que no cumple el contrato | HTTP `400`. En la cola va a la DLQ (reintentarlo no lo arreglaría). |
| Error transitorio de M8 (por ejemplo, su base caída) | En la cola se reencola tras 2 s; después de 5 entregas va a la DLQ. Nunca queda girando para siempre. |
| RabbitMQ caído | M8 sigue atendiendo HTTP y se reconecta solo (espera creciente hasta 30 s). |

La entrega de RabbitMQ es "al menos una vez"; la idempotencia la da la tabla `EventosRecibidos` de M8 (clave primaria `eventoId`).
