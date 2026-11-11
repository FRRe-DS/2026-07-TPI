# Decisiones técnicas: CQRS + Event Sourcing (historial de viajes)

Registro de las decisiones tomadas al implementar la demo de los temas 21 (Event Sourcing) y 13 (CQRS) en M8, Grupo 7. Cada una dice qué se decidió y por qué. Código en `src/M8.Notificaciones.Api/Features/Viajes/`.

## 1. Alcance y organización

| # | Decisión | Por qué |
|---|----------|---------|
| 1.1 | **Un solo escenario integrado**: el historial de viajes. La escritura usa Event Sourcing y la lectura usa CQRS. | La rúbrica premia la integración de ambos temas. Además M8 ya recibe estos hitos de M5 y M6 por `POST /api/v1/eventos`: guardarlos como eventos le da a M8 su propio historial de auditoría (trazabilidad, RF-8.6) sin leer la base de otro módulo (RNF-05). |
| 1.2 | Carpetas separadas: `Dominio/`, `EventStore/`, `Comandos/`, `Consultas/`, `Proyecciones/`, `Controllers/`, `Dtos/`. | En la demo y en el coloquio se puede señalar "esto es Event Sourcing" y "esto es CQRS" por carpeta. |
| 1.3 | Solo ASP.NET Core, EF Core y xUnit. Sin MediatR, Marten ni AutoMapper. | El código tiene que poder explicarse línea por línea. El handler es una clase común que el controlador llama directo. |
| 1.4 | Cambios en código compartido solo **aditivos**: una línea en `Program.cs` (`AddViajesCqrsEs`), tres `DbSet` y una línea (`ViajesModelo.Configurar`) en `M8DbContext`, una migración nueva, claves nuevas en `appsettings.json`, `docker-compose.yml` y `.env.example`. | No tocar el código de Notificaciones, Soporte ni Documentos (otras células). |
| 1.5 | La rama `feature/cqrs-es` sale de `tests-cobertura` y no de `main`. | `main` (actualizado, igual a `origin/main`) todavía no tiene el commit de tests y cobertura (`coverlet.runsettings`, `.config/dotnet-tools.json`, sección "Cobertura" del README, 17 tests), y el pedido los daba por existentes. `tests-cobertura` es `main` + ese único commit, así que el PR va a incluirlo. Si se mergea antes `tests-cobertura`, el diff queda limpio. |
| 1.6 | No se usó el MCP de Context7 porque no estaba disponible en la sesión. | Se usó la API documentada de EF Core 8 y Npgsql 8, y se verificó contra PostgreSQL real (migración aplicada y prueba de integración). |

## 2. Diferencias con el contrato de M8 (pendientes de proponer por PR a la cátedra)

| # | Diferencia | Detalle | Propuesta |
|---|-----------|---------|-----------|
| 2.1 | `destinatarios` **opcional** en esta feature | El contrato lo exige (`required`). Acá no se lee ni se guarda: trae email y teléfono, y un evento no puede tener datos personales (ver 6.1). | Que sea opcional para los `VIAJE_*` que solo registran historial, o mover los contactos a otro recurso. |
| 2.2 | `detalle.conductorId` es **extensión nuestra** | `DetalleEvento` tiene `additionalProperties: false` y no informa qué conductor se asignó. Sin ese dato `ConductorAsignado` no tiene sentido. | Agregar `conductorId` (uuid) a `DetalleEvento`, obligatorio para `VIAJE_ASIGNADO`. |
| 2.3 | `409` en `POST /api/v1/eventos` | El contrato no lista `409` para este endpoint. Lo usamos para transiciones inválidas (finalizar sin asignar) y para conflictos de concurrencia. | Agregar la respuesta `409 Conflicto` al endpoint. |
| 2.4 | La respuesta `202` agrega campos | El contrato exige `eventoId` y `recibidoEn` (están). Agregamos `viajeId`, `version` y `registradoEn` (igual a `recibidoEn`). | Compatible: son campos adicionales. |
| 2.5 | Endpoints nuevos | `GET /api/v1/viajes/{viajeId}/estado`, `/eventos` y `/historial` no están en el contrato. | Proponerlos si el equipo decide adoptar el historial. |
| 2.6 | Endpoints de laboratorio | `GET /api/v1/lab/proyeccion/estado` y `POST /api/v1/lab/proyeccion/reconstruir` son de diagnóstico y administración, **fuera del contrato** a propósito. En producción irían con rol OPERADOR. | No se proponen al contrato. |
| 2.7 | Tipos no soportados → `422` | Tipos válidos del contrato que esta feature no modela (`VIAJE_CANCELADO`, `RESERVA_*`, `PAGO_*`...) devuelven `422 /errores/evento-no-soportado`. | Cuando el endpoint se integre con Notificaciones, esos tipos se derivan a su handler. |
| 2.8 | Sin autenticación | El contrato pide `Bearer` con rol SERVICIO (M1). En el TPI todavía no existe; se simula en una entrega futura. **No está implementada.** | — |
| 2.9 | Sin `X-Request-Id` | La correlación del contrato no se implementó en esta feature. | Middleware común para todo M8. |
| 2.10 | `origen` se valida contra el enum (`M5`, `M6`, `M7`, `M9`), pero no se exige que `VIAJE_FINALIZADO` venga de M6. | Para la demo alcanza; exigirlo es una regla extra fácil de agregar en `Validar`. | — |

## 3. Event Sourcing (lado de escritura)

| # | Decisión | Por qué |
|---|----------|---------|
| 3.1 | Agregado `Viaje` en C# puro: comandos (`Solicitar`, `AsignarConductor`, `Finalizar`) que validan y producen un evento, y `Aplicar(evento)` por tipo, que no valida. | Se testea con dado/cuando/entonces sin base ni HTTP. `Aplicar` no valida porque un evento guardado es un hecho: si validara, un cambio de reglas rompería la lectura de eventos viejos. |
| 3.2 | Reglas: `ViajeSolicitado` solo con flujo vacío; `ConductorAsignado` solo en SOLICITADO; `ViajeFinalizado` solo en ASIGNADO. Lo demás es `409 /errores/transicion-de-viaje-invalida`. | Lo pide la consigna. Por eso también falla "asignar dos veces" o "asignar un viaje finalizado". |
| 3.3 | En el store, `Tipo` guarda el **nombre de dominio** (`ConductorAsignado`) y no el del contrato (`VIAJE_ASIGNADO`). | El event store habla el idioma del dominio; la traducción del contrato está en el borde (handler). Si el contrato renombra un tipo, los eventos guardados no cambian. |
| 3.4 | `Payload` como texto (no `jsonb`). | Los tests con EF InMemory se comportan igual. En producción `jsonb` permitiría consultar dentro del JSON. |
| 3.5 | Tabla append-only: `EventStoreViajes` solo tiene `LeerFlujoAsync`, `BuscarPorEventoIdAsync` y `AgregarAsync`. | No existe forma de editar o borrar un evento desde el código. En producción se reforzaría con permisos de base (solo `INSERT`/`SELECT`). |
| 3.6 | Fechas en UTC y truncadas a microsegundos (`FechasUtc.Normalizar`). | Npgsql exige offset 0 para `timestamptz`. PostgreSQL guarda microsegundos y .NET ticks de 100 ns: sin truncar, la comparación de idempotencia daba un falso 422 al releer la fecha de la base. |
| 3.7 | Idempotencia por `eventoId`: mismo cuerpo → la respuesta original (idéntica, incluido `registradoEn`); otro cuerpo → `422`. El "cuerpo" se compara campo a campo: tipo, viajeId, origen, ocurridoEn y conductorId. | Es la regla del contrato. `destinatarios` no entra en la comparación porque no se guarda (6.1). |
| 3.8 | Concurrencia optimista con índice único `(ViajeId, Version)`: chequeo previo en código + índice real en PostgreSQL. Si falla → `409 /errores/conflicto-de-concurrencia`. | EF InMemory no hace cumplir índices, así que el chequeo previo permite testearlo; el índice es la defensa real ante la carrera (probado con la prueba de integración contra PostgreSQL: 8 asignaciones simultáneas, gana una). |
| 3.9 | Dos envíos simultáneos del **mismo** `eventoId` pueden dar `409` en uno de ellos (choca con el índice único de `EventoId`). | Al reintentar, ese envío cae en la rama idempotente y recibe la respuesta original. No vale la pena más complejidad para la demo. |
| 3.10 | **Sin snapshots.** | Un viaje tiene como mucho un puñado de eventos: reproducirlos cuesta microsegundos. Los snapshots se justifican en flujos largos (cientos o miles de eventos). |
| 3.11 | `TimeProvider` (de .NET 8) como reloj inyectable. | Los tests controlan "ahora" sin `Thread.Sleep`. No hace falta ninguna librería. |

## 4. CQRS (lado de lectura)

| # | Decisión | Por qué |
|---|----------|---------|
| 4.1 | Vista `ViajesHistorialVista`: una fila por evento, con PK `(ViajeId, Version)` y columnas pensadas para mostrar (`Descripcion`, `EstadoResultante`, `ConductorId`). | El endpoint de historial hace un `SELECT` simple, sin reproducir eventos ni joins. La PK es a la vez el índice único que vuelve idempotente la proyección. |
| 4.2 | `GET /historial` lee **solo** la vista, nunca `EventosViaje`. Si no hay nada proyectado, responde `200` con `items: []` (no `404`). | Es la separación de CQRS. El 200 vacío es justamente lo que muestra la consistencia eventual en la demo. |
| 4.3 | `retrasoMs = proyectadoEn - registradoEn`, calculado en la consulta. | Mide de verdad cuánto tardó el evento en volverse consultable. |

## 5. Proyección asíncrona

| # | Decisión | Por qué |
|---|----------|---------|
| 5.1 | `ProyectorHistorialViajes` (BackgroundService) cada 500 ms crea un scope y llama a `ProyeccionHistorialViajes.ProyectarPendientesAsync(ahora)`. | El BackgroundService es singleton y el DbContext es scoped: un scope por pasada da un DbContext fresco. La lógica queda en una clase testeable que recibe "ahora". |
| 5.2 | Un evento se proyecta solo si `RegistradoEn <= ahora - RetrasoMs` (default 3000 ms, variable `Proyeccion__RetrasoMs`). | Hace el retraso **configurable y determinista** para mostrarlo. En un sistema real ese retraso lo producen la cola de mensajes y la carga del lector; acá lo simulamos a propósito. |
| 5.3 | Se recorre por `Seq` y se **corta en el primer evento todavía nuevo** (no se saltea para seguir con los siguientes). | El checkpoint nunca avanza dejando un evento atrás. |
| 5.4 | Filas de la vista + checkpoint en **un solo `SaveChanges`**. | EF Core ejecuta cada `SaveChanges` dentro de una transacción: o se guardan las dos cosas o ninguna. Se evitó una transacción explícita porque EF InMemory no las soporta. |
| 5.5 | Proyección idempotente: si la fila `(ViajeId, Version)` ya existe, se saltea. | Si el proyector se cae entre guardar y avanzar el checkpoint (o se pierde el checkpoint), reprocesar no duplica. |
| 5.6 | `SemaphoreSlim` estático para que el proyector y la reconstrucción no escriban la vista a la vez. | Alcanza con una instancia de la API. Con varias instancias haría falta un lock de base (por ejemplo, `pg_advisory_lock`). |
| 5.7 | `POST /lab/proyeccion/reconstruir`: (1) vacía la vista y pone el checkpoint en 0; (2) reproyecta todo **sin retraso**. Usa `RemoveRange` y no `ExecuteDelete`. | Demuestra que la vista es descartable. Sin retraso porque es una operación de administración. `ExecuteDelete` no funciona con EF InMemory; para el volumen de la demo da igual. Si se corta entre (1) y (2), el proyector sigue desde 0 y la vista se completa sola. |
| 5.8 | **No se usa RabbitMQ** (aunque está en el compose). La proyección hace polling de la tabla. | Es lo más simple de explicar y probar, y no agrega piezas que puedan fallar en vivo. La evolución natural (TP2) es publicar cada evento en la cola, con patrón outbox para no perder ninguno, y que el proyector se suscriba. Checkpoint, idempotencia y vista descartable no cambian. |
| 5.9 | **Limitación conocida**: con escrituras concurrentes, una transacción con `Seq` menor puede confirmarse después que otra con `Seq` mayor. El proyector podría avanzar el checkpoint y no ver ese evento. | Con el retraso de 3 s una transacción que tarda menos que eso no se pierde en la práctica. La solución robusta es un outbox o leer por `xmin`/marcas de commit. Queda documentado para el coloquio. |
| 5.10 | El log `Microsoft.EntityFrameworkCore.Database.Command` se bajó a `Warning` en `appsettings.json`. | El polling cada 500 ms imprimía 4 consultas SQL por pasada y volvía ilegible la consola. Afecta solo al detalle del SQL logueado, no al comportamiento. |

## 6. Privacidad y seguridad

| # | Decisión | Por qué |
|---|----------|---------|
| 6.1 | Los eventos guardan **solo IDs** (`viajeId`, `conductorId`), nunca email, teléfono ni nombres (RNF-19). | Los eventos son inmutables y no se borran: un dato personal guardado ahí no se podría suprimir después (derecho de supresión). Los datos personales viven en su módulo dueño y se consultan por id. Si algún día hiciera falta, la técnica conocida es "crypto-shredding" (cifrar con una clave por persona y borrar la clave). |
| 6.2 | Ningún secreto en archivos versionados. `Proyeccion__RetrasoMs` no es secreto y tiene default en `docker-compose.yml`. | `.env` sigue en `.gitignore`. |

## 7. Pruebas, herramientas y entorno

| # | Decisión | Por qué |
|---|----------|---------|
| 7.1 | Prueba de integración contra PostgreSQL real **opcional**: corre solo si existe `M8_TEST_POSTGRES` (atributo `[PostgresFact]`); si no, queda "omitida". Crea una base temporal, aplica las migraciones y la borra al final. | La CI no tiene PostgreSQL y tiene que seguir en verde. Localmente prueba lo que InMemory no puede: migración, índices únicos y `timestamptz`. |
| 7.2 | `dotnet-ef` 8.0.8 como herramienta local en `.config/dotnet-tools.json`. | Lo pide el enunciado, y cualquiera del grupo lo instala con `dotnet tool restore`. |
| 7.3 | `TestResults/` agregado al `.gitignore`. | `dotnet test --collect` lo generaba dentro del proyecto de tests. |
| 7.4 | La advertencia `MSB3277` (conflicto entre EF Core Relational 8.0.4 y 8.0.8 en el proyecto de tests) **ya existía** antes de esta rama. | No se tocó para no cambiar versiones de paquetes de las otras células. Se arregla alineando las versiones de EF Core. |
| 7.5 | Script `scripts/demo-cqrs-es.ps1` con `-SinPausa` (sin `Read-Host`) para poder guardar la salida en un archivo. | Las evidencias de `docs/evidencias/` se generaron así. |
| 7.6 | En el script, el cuerpo de los errores se lee con `$_.ErrorDetails.Message` y, si viene vacío, del stream de la respuesta. | En Windows PowerShell 5.1, con respuestas *chunked*, `ErrorDetails.Message` llegaba vacío. |
| 7.7 | El script se probó en **Windows PowerShell 5.1**. PowerShell 7 no estaba instalado en la máquina de desarrollo. | Está escrito con comandos compatibles con las dos versiones, pero **falta probarlo en 7**. |
| 7.8 | Opcional no hecho: la página estática con línea de tiempo en `wwwroot`. | Necesitaba agregar `UseStaticFiles` en `Program.cs`. Se priorizó el script, que ya muestra el retraso segundo a segundo. |
