# TP2 - Notificaciones (M8, Grupo 7)

Registro de todo lo hecho para el TP2 de la parte de **Notificaciones** de M8, en el orden en que se hizo, con el porqué de cada decisión. Al final hay una revisión del TP1 con los problemas encontrados en el resto del módulo.

Fecha: 18/11/2026. Punto de partida: la carpeta local, idéntica a la rama `exposicion` de `FRRe-DS/2026-07-TPI` (que ya incluía la demo de CQRS + Event Sourcing).

---

## 0. Qué pide el TP2 y cómo lo leí

La consigna (`FRRe-DS/2026-TPI`, README) no tiene un enunciado separado para el TP2: lo define la columna **Etapa** de los requerimientos no funcionales. Los que caen en TP2 son:

| RNF | Qué pide | Dónde quedó resuelto (sección) |
|---|---|---|
| RNF-05 Propiedad de datos | Cada módulo es dueño de su base; nada de leer tablas ajenas | 5, 6 (los datos de otros módulos llegan en el evento) |
| RNF-07 Pruebas | Integración **distribuida** en TP2 | 13, 14 |
| RNF-08 Concurrencia | Nada doble ante solicitudes concurrentes | 6, 8, 9 |
| RNF-09 Idempotencia | Operaciones críticas y consumidores toleran reintentos | 6, 7, 10 |
| RNF-10 Mensajería | RabbitMQ para los flujos asíncronos | 10 |
| RNF-11 Caché y vencimiento | Redis para estado efímero con TTL | 7 |
| RNF-12 Seguridad | Autenticación, autorización, validación | 3 |
| RNF-13 Resiliencia | Timeouts, reintentos limitados, nada de bloqueos indefinidos | 2, 8, 9, 10 |
| RNF-14 Salud y diagnóstico | Health checks, logs estructurados, correlación | 2, 11 |
| RNF-17 Automatización | Build y pruebas automáticas | 14 |

Y de los funcionales de M8, los de Notificaciones son **RF-8.1** (avisos de viaje), **RF-8.2** (avisos de reserva), **RF-8.6** (seguimiento de entrega y reintentos) y **RF-8.8** (consumo asíncrono desde una cola).

El **contrato** de M8 (`contratos/m8_notificaciones/openapi.yml`, acordado con el Grupo 10) ya decía cómo tenía que funcionar todo esto: roles, `X-Request-Id`, `problem+json`, `Idempotency-Key`, "como máximo 3 intentos con espera creciente", y que en el TP2 "los mismos eventos viajan por la cola y este endpoint se mantiene". Lo tomé como la especificación a cumplir al pie de la letra. Nuestra copia `docs/m8-openapi.yaml` es idéntica a la de la cátedra (solo difiere un salto de línea final).

Del Keycloak de la cátedra (`keycloak/realm-config/ds-2026-realm.json`) saqué un dato clave: **los roles del realm no son los del contrato**. Cada client `grupo-XX` tiene un service account con un rol `*-be` (`viajes-be`, `reservas-be`, `notificaciones-be`...). No existen usuarios con rol CLIENTE, CONDUCTOR u OPERADOR. Eso define cómo hice la autorización (sección 3).

---

## 1. Revisión del punto de partida

- Compilé y corrí los tests antes de tocar nada: **81 correctos, 2 omitidos** (los de PostgreSQL).
- La parte de Notificaciones del TP1 era mínima y **no seguía el contrato**: ruta `POST /api/Notificaciones/enviar` (el contrato dice `/api/v1/notificaciones`), guardaba la notificación directamente como "Enviado" sin enviar nada, el destinatario era un texto libre (email) en vez de `destinatarioId` + `rol`, no había intentos, ni reintentos, ni listado, y los enums salían como números en el JSON.
- `POST /api/v1/eventos` (el endpoint del contrato por donde M5, M6, M7 y M9 avisan los hitos) estaba ocupado por la demo de Event Sourcing, que solo aceptaba 3 tipos de viaje y respondía `422` a todos los demás (reservas, pagos, cancelaciones...). Con eso, M8 no podía cumplir RF-8.1 ni RF-8.2.

Decidí **reemplazar** la parte de Notificaciones del TP1 por una implementación nueva que siga el contrato, y **compartir** `POST /api/v1/eventos` con la demo ES sin romperla (sección 5).

---

## 2. Infraestructura HTTP común (lo que el contrato pide para todo M8)

Carpeta `src/M8.Notificaciones.Api/Infrastructure/Http/` y `Infrastructure/Salud/`.

1. **`RequestIdMiddleware`** (RNF-14). Lee `X-Request-Id`; si cumple `^[A-Za-z0-9._-]{1,128}$` lo reutiliza, si no genera uno (nunca responde 400 por esto, como dice el contrato). Lo devuelve en **toda** respuesta, incluidos los errores, y abre un *scope* de logging con `RequestId`: cada línea de log de esa solicitud queda correlacionada. Va primero en el pipeline para que hasta un 401 o un 500 lleven el encabezado.
2. **`ProblemasHttp`**: todos los errores salen en `application/problem+json` (RFC 9457). Los genéricos con `type: about:blank` y el título exacto del contrato en español ("No autenticado", "Sin permiso", "Método no permitido"...). Los de dominio (409, 422) con `type` propio (`/errores/evento-id-reutilizado`, `/errores/notificacion-no-reintentable`...). Incluye:
   - el `400` de ASP.NET por cuerpo mal formado, reemplazado por uno con el formato del contrato;
   - un `UseStatusCodePages` que completa con problem+json las respuestas que salen sin cuerpo (401 y 403 de la autenticación, 404 de rutas inexistentes, 405 con `Allow`, 415);
   - un manejador global de excepciones: si se cae PostgreSQL o Redis responde **503 con `Retry-After`**; cualquier otra cosa, 500 sin detalles internos (RNF-13).
3. **`LimiteCuerpoMiddleware`**: cuerpos de más de 1 MiB → `413` (contrato).
4. **`GET /health`** (`SaludEndpoints`): sin token, `200 {"estado":"ok"}` si la base responde en menos de 3 s; si no, `503`. Nunca queda colgado.

## 3. Seguridad con el Keycloak de la cátedra (RNF-12)

Carpeta `Infrastructure/Seguridad/`.

- **JWT Bearer**: M8 valida firma (con las claves públicas JWKS del realm), emisor y vencimiento. **No guarda ningún secreto**: no le hace falta para validar. Todo se configura de afuera (sección `Auth`, RNF-06).
- **Problema de Docker**: el token dice `iss = http://localhost:8080/...`, pero desde el contenedor de M8 "localhost" es el propio contenedor. Por eso separé `Emisor` (lo que se valida en el token) de `MetadataAddress` (de dónde se bajan las claves: `host.docker.internal:8080`). Así funciona igual dentro y fuera de Docker.
- **`TransformacionRoles`**: Keycloak pone los roles en el claim `realm_access` como JSON, que ASP.NET no entiende solo. Esta clase lo lee y lo traduce a los roles del contrato con una tabla configurable (`Auth:MapeoRoles`):
  - `solicitud-be` (M5), `viajes-be` (M6), `tarifas-be` (M7), `reservas-be` (M9), `notificaciones-be` → **SERVICIO**;
  - `clientes-be` (M2) o cualquier otro **no** da permisos (M2 no informa hitos);
  - los roles que ya se llaman como en el contrato (CLIENTE, CONDUCTOR, OPERADOR, SERVICIO) pasan directo, para cuando M1 los emita.
- **OPERADOR en desarrollo**: como el realm no tiene a nadie con ese rol, en `docker-compose.yml` el client `grupo-07` recibe también OPERADOR (`Auth__MapeoRoles__notificaciones-be__1=OPERADOR`), solo para poder probar los endpoints de operador. Está marcado como "solo desarrollo".
- Cada endpoint declara sus roles con `[Authorize(Roles = ...)]` exactamente como en el `security` del contrato.
- Swagger tiene el botón **Authorize** para pegar el token (`scripts/obtener-token.ps1` lo obtiene y lo copia al portapapeles).

## 4. Modelo de datos de Notificaciones

Carpeta `Features/Notificaciones/Dominio` y `Entities`.

- **Enums con los nombres exactos del contrato** (`EMAIL`, `PENDIENTE`, `VIAJE_ARRIBO_CONDUCTOR`...), que se serializan como texto y **rechazan números** (antes salían como 0, 1, 2).
- **`Notificacion`**: destinatario (`DestinatarioId` + `Rol`), canal, asunto, mensaje, referencia (viaje/reserva/pago), estado, `Intentos`, `MaxIntentos`, `ProximoIntentoEn`, `BloqueadaHasta` (lease), `Version` (concurrencia optimista), `UltimoError`.
  - `Contacto` guarda el email/teléfono a usar. Es dato personal: **nunca** se devuelve por la API ni se loguea, y **se borra apenas la notificación queda ENVIADA** (RNF-19).
- **`IntentoNotificacion`**: historial de intentos que solo crece (RF-8.6); `(NotificacionId, Numero)` es único.
- **`ContactoDestinatario`**: el último email/teléfono conocido de cada usuario, aprendido de los eventos. Hace falta porque el contrato dice que las notificaciones manuales y los reenvíos van "al contacto registrado, nunca a una dirección de la solicitud", y M8 no puede preguntarle a otro módulo (RNF-05).
- **`EventoRecibido`** (bandeja de entrada, patrón *inbox*): un registro por `eventoId` con el hash del cuerpo. Es la base de la idempotencia (sección 6).
- **Migración `NotificacionesTp2`** (editada a mano): EF proponía convertir columnas de la tabla vieja (por ejemplo `ReferenciaId` de texto a uuid), lo que falla si hay datos de prueba como `"viaje-123"`. Como la tabla del TP1 tenía otra forma y solo datos de prueba, la migración la **elimina y la crea de nuevo**; `Down` restaura la del TP1.

## 5. `POST /api/v1/eventos`: la entrada de los hitos (RF-8.1, RF-8.2)

Carpeta `Features/Eventos/`.

- **`EventoEntrada`**: el DTO con la forma exacta del esquema del contrato. **El mismo formato sirve para HTTP y para RabbitMQ.**
- **`ValidadorEvento`**: todas las reglas del esquema que el deserializador no controla: obligatorios, `anyOf` de `viajeId/reservaId/pagoId`, qué id exige cada tipo (`VIAJE_*` → `viajeId`; `RESERVA_*` → `reservaId`; pagos → `pagoId` **y** `viajeId`), email válido, teléfono E.164, 1 a 5 destinatarios, canales sin repetir, largos, moneda ISO 4217, zona horaria IANA.
  - Única licencia: `destinatarios` se acepta ausente (el evento se registra y no genera avisos), porque el grupo ya lo había decidido así para el historial de viajes (DECISIONES 2.1).
- **`EventosController`**: solo rol SERVICIO, responde `202 {eventoId, recibidoEn}` sin enviar nada todavía (el envío es asíncrono, sección 8).
- **Convivencia con la demo ES**: el endpoint del contrato ahora es de Notificaciones y acepta los 15 tipos. El historial de viajes se sigue alimentando como una **reacción** (`ReaccionHistorialViajes`), que corre después de registrar el evento: si sus reglas lo rechazan (por ejemplo "finalizar sin asignar"), solo se loguea y el aviso se manda igual (M8 tiene que notificar aunque el historial no lo acepte). La demo original, con sus `409` y `422`, quedó en `POST /api/v1/lab/viajes/eventos` y actualicé `scripts/demo-cqrs-es.ps1` para que use esa ruta. `IReaccionEvento` también le sirve a la célula de Documentos para disparar el comprobante en `PAGO_CAPTURADO` sin tocar la ingesta.

## 6. La ingesta: idempotencia y atomicidad (RNF-08, RNF-09)

`Features/Eventos/IngestaEventos.cs`. La usan los dos caminos de entrada (HTTP y RabbitMQ), así que se comportan igual.

1. Valida.
2. Normaliza las fechas a UTC con microsegundos y calcula un **hash SHA-256 del cuerpo**: así `15:00Z` y `12:00-03:00` son el mismo evento.
3. Si el `eventoId` ya está en `EventosRecibidos`: mismo hash → devuelve la **respuesta original** (mismo `recibidoEn`); otro hash → `422`.
4. Arma **una notificación por destinatario** y guarda evento + notificaciones en **un solo `SaveChanges`** (una transacción): o queda todo o nada.
5. **Carrera**: si el mismo evento entra a la vez por dos lados (dos HTTP, o HTTP y la cola), la clave primaria de `EventosRecibidos` deja pasar uno solo; el otro recibe la excepción, relee el ganador y responde como duplicado. Además hay un índice único `(EventoId, DestinatarioId, Rol)` en `Notificaciones` como segunda barrera. **Probado contra PostgreSQL real con 8 entregas simultáneas: 1 registrada, 7 duplicadas, 2 notificaciones.**
6. Pasos secundarios que no afectan la respuesta: recordar el contacto de cada destinatario y ejecutar las reacciones.

**Elección de canal** (`SelectorCanal`): se recorren los `canalesPreferidos` en orden (si no hay, primero `CanalPorDefecto` = EMAIL y después los demás) y se toma el primero para el que haya contacto (EMAIL necesita email, SMS teléfono, PUSH va por `usuarioId`). Si el evento no trae el contacto, se usa el registrado. Si ningún canal sirve, la notificación nace **FALLIDA** con "Sin contacto registrado..." (lo dice el contrato).

**Mensajes** (`PlantillasMensajes`): asunto y texto en español para los 15 tipos y los 2 roles, usando el `detalle` (origen → destino, fecha de la reserva **convertida a su zona horaria**, motivo de cancelación, importe con formato argentino).

## 7. `Idempotency-Key` con Redis (RNF-09, RNF-11)

`Infrastructure/Idempotencia/`.

- Los POST manuales (`/notificaciones` y `/reintentos`) exigen `Idempotency-Key` (sin ella → `400`).
- **`AlmacenIdempotenciaRedis`**: reservar la clave es **atómico** (`SET NX`): si llegan 10 solicitudes iguales a la vez, una sola ejecuta; las otras reciben `409 /errores/idempotencia-en-curso`. Completada, la respuesta se guarda **24 h con TTL** y Redis la borra sola. Mismo cuerpo → misma respuesta; otro cuerpo → `422`. La clave se guarda **hasheada** y separada por quien llama (dos clientes no chocan).
- **`EjecucionIdempotente`** envuelve el endpoint: si la operación falla (400, 404, 409...), la clave se libera para que el cliente pueda corregir y reintentar con la misma.
- Si Redis no responde → `503` (no se cuelga: timeouts de 5 s).

## 8. Envío asíncrono con reintentos (RF-8.6, RNF-13)

`Features/Notificaciones/Envio/`.

- **`DespachadorNotificaciones`** (BackgroundService): cada segundo le pide a `ServicioEnvio` que procese las notificaciones `PENDIENTE` cuyo `ProximoIntentoEn` ya llegó. Si la base se cae, espera 5 s y sigue, sin tumbar la API.
- **`ServicioEnvio`**, por cada notificación:
  1. **La toma** con un *lease* (`BloqueadaHasta`) guardado con **concurrencia optimista** (`Version`). Si otra instancia de M8 la tomó primero, la saltea. **Probado con 4 despachadores simultáneos sobre PostgreSQL: cada aviso salió una sola vez.**
  2. Llama al proveedor con **timeout** (10 s): un proveedor que no responde se corta y cuenta como intento fallido.
  3. Registra el intento y actualiza: éxito → `ENVIADA` (y borra el contacto); error con intentos disponibles → sigue `PENDIENTE` con **espera creciente** (5 s y 30 s); error en el 3.er intento → `FALLIDA`.
  - Garantía: "al menos una vez" (si la API muere justo después de que el proveedor aceptó y antes de guardar, el lease vence y se reenvía). Está documentado.
- **Proveedores** (servicios externos "reales o simulados según la etapa", dice la consigna):
  - **EMAIL real por SMTP** hacia **Mailpit**, una bandeja de prueba que agregué al compose (<http://localhost:8025>): se ven los correos que manda M8.
  - **SMS y PUSH simulados** (registran el envío en el log, sin el contacto).
  - Para poder **mostrar fallos y reintentos**, cualquier contacto que contenga `falla` (por ejemplo `cliente@falla.test`) es rechazado; también se puede configurar una probabilidad de fallo al azar.
  - Los errores que se guardan nunca contienen el email ni el teléfono (del SMTP solo se guarda el código).

## 9. Endpoints de Notificaciones

`Features/Notificaciones/Controllers/NotificacionesController.cs`, todos según el contrato:

| Endpoint | Rol | Detalle |
|---|---|---|
| `GET /api/v1/notificaciones` | OPERADOR, SERVICIO | Filtros `destinatarioId`, `estado`, `canal`, `tipoEvento`, `referenciaId`; `page` desde 0, `size` 1 a 100 (si no, `400`); más recientes primero. |
| `POST /api/v1/notificaciones` | OPERADOR | Aviso manual. `Idempotency-Key`. Usa el contacto registrado. `202` + `Location`. |
| `GET /api/v1/notificaciones/{id}` | OPERADOR, SERVICIO | Un id que no es UUID → `400`; inexistente → `404`. |
| `GET /api/v1/notificaciones/{id}/intentos` | OPERADOR, SERVICIO | Paginado, en orden cronológico. |
| `POST /api/v1/notificaciones/{id}/reintentos` | OPERADOR | Solo si está `FALLIDA` (si no, `409`). Vuelve a `PENDIENTE` con **exactamente un intento más**; no borra los anteriores. Dos operadores a la vez → uno solo gana (concurrencia optimista). |

## 10. Consumo desde RabbitMQ (RF-8.8, RNF-10)

`Infrastructure/Mensajeria/ConsumidorEventosRabbitMq.cs`. Catálogo y topología en [`CATALOGO_EVENTOS.md`](CATALOGO_EVENTOS.md).

- Al arrancar declara (idempotente): exchange `movilidad.eventos` (topic) → cola **`m8.eventos` (quorum, `x-delivery-limit = 5`)** con bindings `viaje.#`, `reserva.#`, `pago.#`, `reintegro.#` → *dead-letter* a `m8.eventos.dlx` → **`m8.eventos.dlq`**.
- Confirmación **manual** de cada mensaje:
  - nuevo o repetido → `ack` (la idempotencia la da la ingesta: RabbitMQ entrega "al menos una vez");
  - JSON roto, fuera de contrato o `eventoId` reutilizado → `nack` sin reencolar → **DLQ** (reintentarlo no lo arreglaría);
  - error transitorio (base caída) → espera 2 s y reencola; la cola quorum cuenta las entregas y a la 5.ª lo manda a la DLQ: **nunca queda girando para siempre**.
- Si RabbitMQ no está, la API arranca igual (el HTTP sigue funcionando) y se reconecta con espera creciente hasta 30 s.
- El `correlation_id` del mensaje se usa como `RequestId` en los logs.
- Usé `RabbitMQ.Client` 7 (API asíncrona).

## 11. Logs y privacidad (RNF-14, RNF-19)

- En Docker los logs salen en **JSON** con los scopes (`RequestId`, `EventoId`, `RoutingKey`), listos para filtrar por solicitud, viaje o evento.
- Verificado en la prueba de punta a punta: el `X-Request-Id: demo-tp2-001` aparece en las líneas de log de esa solicitud, y en todo el log **no aparece ningún email ni teléfono** (0 coincidencias).

## 12. Configuración y Docker (RNF-04, RNF-06)

- `appsettings.json`: secciones nuevas `Auth`, `RabbitMQ` y `Notificaciones` con valores por defecto razonables y **sin secretos**.
- `docker-compose.yml`:
  - healthchecks en RabbitMQ y Redis, y la API espera a que estén sanos (`service_healthy`), no solo "iniciados";
  - healthcheck de la propia API contra `/health`;
  - servicio **Mailpit** nuevo;
  - variables de seguridad apuntando al Keycloak de la cátedra y `extra_hosts: host.docker.internal`.
- `.env.example`: variables opcionales de seguridad y `KEYCLOAK_CLIENT_SECRET` (vacío: no se versiona).
- **Versiones de EF Core alineadas**: subí `Npgsql.EntityFrameworkCore.PostgreSQL` de 8.0.4 a 8.0.8. Eso eliminó la advertencia `MSB3277` que venía del TP1 (DECISIONES 7.4). El build queda con **0 advertencias**.
- Paquetes nuevos: `RabbitMQ.Client` 7.1.2, `Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.11 y, en tests, `Microsoft.AspNetCore.Mvc.Testing` 8.0.11.

## 13. Pruebas (RNF-07)

Carpeta `tests/M8.Notificaciones.Tests/Notificaciones/`. Reemplacé `NotificacionesControllerTests.cs` del TP1 (probaba la ruta vieja).

| Archivo | Qué prueba | Cómo |
|---|---|---|
| `ReglasTests.cs` | Validación del evento, elección de canal, plantillas de los 15×2 casos, traducción de roles, formato de `X-Request-Id`, esperas crecientes, claves de enrutamiento | Unitarias puras |
| `IngestaYEnvioTests.cs` | Ingesta (nuevo, duplicado, otro huso horario, `422`, inválido, contacto registrado, reacción que falla, historial ES), envío (éxito, 3 fallos con esperas, timeout, lease, concurrencia optimista), proveedores | EF InMemory + reloj controlado (sin `Thread.Sleep`) |
| `ApiNotificacionesTests.cs` | La API completa por HTTP: 401/403 por rol y token vencido o de otro emisor, 202/422/400/413/415/405, `X-Request-Id`, paginación, 404, `Idempotency-Key` (400/202/422), contacto registrado, reintento completo FALLIDA → PENDIENTE → ENVIADA | `WebApplicationFactory` con tokens JWT firmados por el test |
| `InfraestructuraRealTests.cs` | **Integración distribuida**: RabbitMQ (publicar dos veces, JSON roto y evento inválido → 2 notificaciones y 2 mensajes en la DLQ), Redis (10 reservas simultáneas → 1 sola; TTL; clave hasheada), PostgreSQL (migración, 8 ingestas y 4 despachadores simultáneos) | Contra los servicios reales. Se activan con `M8_TEST_RABBITMQ`, `M8_TEST_REDIS` y `M8_TEST_POSTGRES` (si no, quedan omitidas) |

Resultados reales (en `docs/evidencias/tests-tp2-salida.txt`): **143 de 143 correctas, 0 omitidas** con la infraestructura en Docker (después agregué un test más del proveedor SMTP: 144). Sin las variables quedan 139 correctas y 5 omitidas.

Cobertura del código nuevo (Notificaciones + Eventos + infraestructura nueva): **87,4 % de líneas, 96,8 % de métodos.**

## 14. CI (RNF-17)

`.github/workflows/ci.yml`:

- El job de pruebas levanta **PostgreSQL, RabbitMQ y Redis como contenedores de servicio** y define las tres variables: en GitHub las pruebas de integración distribuida corren siempre (con credenciales descartables que solo existen en el runner).
- Job nuevo que **construye la imagen Docker** de M8 (RNF-04).

## 15. Prueba de punta a punta real

Con el **Keycloak de la cátedra** levantado desde el repo `2026-TPI` y la API nueva corriendo, `scripts/demo-notificaciones.ps1` (salida en `docs/evidencias/demo-notificaciones-salida.txt`) mostró:

1. Sin token → `401` problem+json. Token real del client `grupo-07` → aceptado.
2. `VIAJE_ASIGNADO` por HTTP → `202`; el mismo evento otra vez → `202` con el mismo `recibidoEn`.
3. `RESERVA_CONFIRMADA` publicado en RabbitMQ → consumido.
4. Cliente: 2 notificaciones por EMAIL **ENVIADAS** y los 2 correos en Mailpit. Conductor: SMS enviado.
5. `cliente@falla.test`: intento 1 → +5 s intento 2 → +30 s intento 3 → **FALLIDA**; reintento del OPERADOR → `202` PENDIENTE, mismo reintento con la misma clave → respuesta original, cuarto intento registrado; reintentar una ENVIADA → `409`.

Nota: para esta prueba corrí la API con `dotnet run` (puerto 5099) contra una **base de datos aparte** (`m8_e2e_tp2`) y colas con prefijo `e2e.`, porque en tu Docker seguían corriendo los contenedores `m8-*` de una carpeta vieja (`TPI_07\2026-07-TPI\2026-07-TPI`) y no los quise borrar. Ver "Qué te queda hacer".

---

## 16. Revisión del TP1 (lo que encontré en el resto del módulo)

No toqué el código de Soporte ni de Documentos (son de otras células y hay trabajo en curso, por ejemplo la rama `rama_pdf`). Esto es lo que conviene corregir para no arrastrar errores:

### Documentos y QR (Célula 2)

1. **Bug real en el QR**: `QrServices.cs`, línea 19: `TimeSpan.FromMinutes(Math.Clamp(validezSegundos, 30, 3600))`. Usa **minutos** para un valor en **segundos**: un QR pedido por 600 s dura 600 **minutos** (10 horas). Va `TimeSpan.FromSeconds`.
2. La validación del QR está en `POST /api/Documentos/qr/validar` y responde `410`; el contrato dice `POST /api/v1/qr/validaciones` con `404` (no existe), `409` (usado/reemplazado) y `422` (expirado o de otro viaje). Tampoco recibe ni controla el `viajeId`, ni exige `Idempotency-Key`.
3. `imagenPng` se devuelve con el prefijo `data:image/png;base64,`; el contrato pide el base64 solo.
4. El reenvío de comprobante recibe `EmailDestino`; el contrato prohíbe enviarlo a una dirección de la solicitud (solo `canal`, al contacto registrado). Puede usar la tabla `ContactosDestinatarios` y crear una `Notificacion` con `Origen = REENVIO`.
5. El PDF es un mock; rutas `/api/Documentos/comprobantes/...` en vez de `/api/v1/comprobantes/...`.

### Soporte (Célula 3)

6. Resuelto en Soporte. La API usa `/api/v1/tickets`, obtiene el titular del `sub` del JWT y recibe `referencia {tipo, id}`. Incluye listado, cambio de estado e historial.
7. Los enums (`EstadoTicket`, `TipoIncidencia`) salen como **números** en el JSON. Se puede copiar el patrón `EnumContrato<T>` de `Features/Notificaciones/Dominio/Enums.cs`.

### Todo M8

8. Ahora existen la autenticación y la autorización (sección 3), pero **solo están aplicadas** a Eventos y Notificaciones. Las otras células tienen que agregar `[Authorize(Roles = ...)]` en sus controladores según el contrato. Los endpoints de laboratorio de la demo ES siguen abiertos (lo dejé así para no romper el script de la demo).
9. `X-Request-Id`, `problem+json`, `413`, `405` y `/health` ya aplican a todo M8 porque son middlewares comunes. Soporte usa `ProblemasHttp.Generico` para sus respuestas 404.

---

## 17. Qué te queda hacer

1. **Contenedores viejos**: siguen corriendo `m8-notificaciones-api`, `m8-postgres`, `m8-rabbitmq` y `m8-redis` del proyecto compose `2026-07-tpi` (carpeta vieja `TPI_07\2026-07-TPI\2026-07-TPI`). Ocupan los mismos nombres y puertos, así que `docker compose up --build` en esta carpeta falla hasta que los saques. Sus datos están en volúmenes aparte (`2026-07-tpi_*`), que no se pierden:
   ```powershell
   docker stop m8-notificaciones-api m8-postgres m8-rabbitmq m8-redis
   docker rm m8-notificaciones-api m8-postgres m8-rabbitmq m8-redis
   docker compose up --build
   ```
2. **Restos de la prueba de punta a punta** en esos contenedores viejos (no molestan; si querés borrarlos): la base `m8_e2e_tp2` en el Postgres y las colas/exchanges `e2e.*` en RabbitMQ.
3. El **Keycloak de la cátedra** que levanté sigue corriendo (proyecto compose `catedra-keycloak`). Para apagarlo: `docker compose -p catedra-keycloak stop`.
4. Poner `KEYCLOAK_CLIENT_SECRET` (el secreto del client `grupo-07` publicado en el realm de la cátedra) en tu `.env` para usar los scripts.
5. **Acordar con el consorcio** el nombre del exchange (`movilidad.eventos`) y el formato de routing keys de [`CATALOGO_EVENTOS.md`](CATALOGO_EVENTOS.md), y con el Grupo 10 (la otra implementación de M8) para que ambos usen lo mismo.
6. Proponer por PR al contrato: `409` en `POST /api/v1/notificaciones` (clave de idempotencia en curso) y que `destinatarios` pueda faltar.
7. Pasarle a las células 2 y 3 la lista de la sección 16.
8. Hacer commit en una rama (por ejemplo `feature/tp2-notificaciones`) y abrir el PR: la carpeta local no es un repositorio git, así que no hice commits.

---

## Índice de archivos

**Nuevos**

- `src/.../Infrastructure/Http/` — `RequestIdMiddleware.cs`, `ProblemasHttp.cs`, `LimiteCuerpoMiddleware.cs`
- `src/.../Infrastructure/Salud/SaludEndpoints.cs`
- `src/.../Infrastructure/Seguridad/` — `Roles.cs`, `OpcionesAuth.cs`, `TransformacionRoles.cs`, `SeguridadExtensions.cs`
- `src/.../Infrastructure/Idempotencia/` — `AlmacenIdempotencia.cs`, `EjecucionIdempotente.cs`
- `src/.../Infrastructure/Mensajeria/` — `OpcionesRabbitMq.cs`, `ConsumidorEventosRabbitMq.cs`
- `src/.../Infrastructure/Tiempo/Utc.cs`
- `src/.../Features/Eventos/` — `Dtos/EventoEntrada.cs`, `Entities/EventoRecibido.cs`, `Controllers/EventosController.cs`, `ValidadorEvento.cs`, `IngestaEventos.cs`, `ReaccionHistorialViajes.cs`
- `src/.../Features/Notificaciones/` — `Dominio/Enums.cs`, `Entities/IntentoNotificacion.cs`, `Entities/ContactoDestinatario.cs`, `Servicios/` (`FabricaNotificaciones`, `SelectorCanal`, `PlantillasMensajes`), `Envio/` (`Proveedores`, `ServicioEnvio`, `DespachadorNotificaciones`), `OpcionesNotificaciones.cs`, `NotificacionesModelo.cs`, `NotificacionesServiceCollectionExtensions.cs`
- `src/.../Infrastructure/Persistence/Migrations/20261119000543_NotificacionesTp2*.cs`
- `tests/.../Notificaciones/` — `Apoyo/ApiDePrueba.cs`, `Apoyo/Eventos.cs`, `ReglasTests.cs`, `IngestaYEnvioTests.cs`, `ApiNotificacionesTests.cs`, `InfraestructuraRealTests.cs`
- `scripts/demo-notificaciones.ps1`, `scripts/obtener-token.ps1`
- `docs/TP2_NOTIFICACIONES.md` (este archivo), `docs/CATALOGO_EVENTOS.md`, `docs/evidencias/tests-tp2-salida.txt`, `docs/evidencias/demo-notificaciones-salida.txt`

**Modificados**

- `Program.cs` (pipeline y registros), `M8DbContext.cs`, `appsettings.json`, `M8.Notificaciones.Api.csproj`, `M8.Notificaciones.Tests.csproj`
- `Features/Notificaciones/` — `Entities/Notificacion.cs`, `Dtos/NotificacionDtos.cs`, `Controllers/NotificacionesController.cs` (reescritos)
- `Features/Viajes/Controllers/EventosController.cs` (solo la ruta → `/api/v1/lab/viajes/eventos`), `scripts/demo-cqrs-es.ps1` (esa misma ruta)
- `docker-compose.yml`, `.env.example`, `.github/workflows/ci.yml`, `README.md`, `docs/DECISIONES.md` (sección 8)

**Eliminado**

- `tests/M8.Notificaciones.Tests/NotificacionesControllerTests.cs` (TP1, ruta vieja)
