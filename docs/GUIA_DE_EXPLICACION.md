# Guía de explicación para el coloquio: CQRS + Event Sourcing en M8

Para cualquier integrante del Grupo 7. Cada pieza tiene "cómo decirlo en voz alta" en pocas líneas y el archivo donde mostrarla. Al final hay 15 preguntas probables con su respuesta corta.

**La idea en una frase:** M8 recibe los hitos de un viaje (solicitado, asignado, finalizado). En vez de guardar solo "cómo está el viaje ahora", guarda cada hito como un evento que no se modifica nunca (**Event Sourcing**). Para consultar el historial rápido, un proceso aparte arma una tabla de lectura con esos eventos, que se actualiza unos segundos después (**CQRS**).

```
POST /api/v1/eventos ─▶ Handler ─▶ Agregado Viaje ─▶ EventosViaje (append-only)
   (comando)                       (valida reglas)          │
                                                            │ cada 500 ms, con retraso de 3 s
                                                            ▼
GET /historial ◀── ConsultasHistorial ◀── ViajesHistorialVista ◀── Proyector
   (consulta)                              (modelo de lectura)

GET /estado y /eventos ◀── leen EventosViaje directamente y reproducen los eventos
```

---

## 1. Las piezas, una por una

### Agregado `Viaje` (`Features/Viajes/Dominio/Viaje.cs`)
"El viaje no está guardado en ninguna tabla: lo reconstruimos reproduciendo sus eventos en orden. Tiene dos tipos de métodos. Los comandos (`Solicitar`, `AsignarConductor`, `Finalizar`) chequean la regla, por ejemplo que no se pueda finalizar sin conductor, y si pasa devuelven un evento nuevo. `Aplicar` solo cambia el estado en memoria y no valida, porque un evento es algo que ya pasó. No depende de EF ni de ASP.NET, así que se prueba con dado/cuando/entonces."

### Eventos (`Dominio/EventosDeViaje.cs`)
"Son `record` inmutables con nombre en pasado: `ViajeSolicitado`, `ConductorAsignado`, `ViajeFinalizado`. Cuentan qué pasó en el negocio y no qué columna cambió. Llevan solo IDs, ningún dato personal, porque un evento no se puede borrar."

### Event store (`EventStore/EventoAlmacenado.cs`, `EventStoreViajes.cs`)
"Es la tabla `EventosViaje`. Solo se le agregan filas: el código no tiene ni Update ni Delete. Cada fila tiene un `Seq` global (el orden de todo), el `EventoId` que manda M5 o M6 (único, para la idempotencia) y la posición `(ViajeId, Version)`, también única. Ese índice es la concurrencia optimista: si dos comandos quieren escribir la versión 3 del mismo viaje, la base rechaza al segundo y respondemos 409."

### Handler del comando (`Comandos/RegistrarEventoViajeHandler.cs`)
"Es el lado de escritura de CQRS. Hace cinco pasos que se leen de arriba a abajo: valida el cuerpo; si el `eventoId` ya existe, devuelve la respuesta original (o 422 si el cuerpo es otro); reconstruye el viaje leyendo su flujo; le pide al agregado que decida; y agrega el evento con la versión siguiente. No toca la vista de lectura: por eso responde 202 Accepted."

### Proyector (`Proyecciones/ProyeccionHistorialViajes.cs` y `ProyectorHistorialViajes.cs`)
"Es el puente entre la escritura y la lectura. Un BackgroundService, cada medio segundo, busca eventos con `Seq` mayor al checkpoint y registrados hace más de 3 segundos, y los convierte en filas de la vista. Guarda las filas y el checkpoint en la misma transacción. Si una fila ya existe la saltea, así que reprocesar no duplica. La lógica está en una clase que recibe el 'ahora' como parámetro, y por eso la podemos testear sin esperar."

### Vista de lectura (`Proyecciones/ViajeHistorialItem.cs`, tabla `ViajesHistorialVista`)
"Es el modelo de lectura: una tabla ya armada para mostrar, con la descripción, el estado resultante y las fechas. La consulta es un `SELECT` simple. Es descartable: la borramos y la reconstruimos desde los eventos con `POST /lab/proyeccion/reconstruir`, y queda igual."

### Endpoints (`Controllers/`)
"Están separados por lado. Escritura: `POST /api/v1/eventos` (el del contrato). Lectura de Event Sourcing: `/viajes/{id}/estado` reproduce los eventos y `/viajes/{id}/eventos` muestra el flujo crudo; siempre están al día. Lectura CQRS: `/viajes/{id}/historial` lee solo la vista y puede estar atrasado. Laboratorio, fuera del contrato: `/lab/proyeccion/estado` y `/reconstruir`. Los errores salen en `application/problem+json`."

### Tests (`tests/M8.Notificaciones.Tests/Viajes/`)
"Hay 64 tests nuevos además de los 17 que ya había. Los del agregado son dado/cuando/entonces puros. Los del handler prueban idempotencia (202 repetido y 422) y concurrencia (409). Los del proyector mueven un reloj de mentira para probar que no proyecta antes de los 3 segundos y sí después, sin `Thread.Sleep`. Hay dos pruebas contra PostgreSQL real que corren solo si se define `M8_TEST_POSTGRES`: ocho asignaciones simultáneas y gana una sola. La cobertura de `Features/Viajes` es mayor al 90 % (ver `docs/evidencias/cobertura-salida.txt`)."

---

## 2. Cómo correr la demo (para quien la muestre)

1. `docker compose up --build` (la migración se aplica sola al arrancar).
2. En otra terminal: `.\scripts\demo-cqrs-es.ps1` y Enter entre partes.
3. Qué señalar en cada parte:
   - **Parte 1:** las versiones 1, 2 y 3; el payload sin datos personales; el estado reconstruido con `eventosReproducidos: 3`; el 409 al finalizar sin asignar; el mismo `eventoId` dos veces (una sola fila) y con otro cuerpo (422).
   - **Parte 2:** "acabo de mandar los tres eventos y la vista está vacía". Después contar los segundos hasta que aparecen. Mostrar `retrasoMs` alrededor de 3000 y `pendientes` que baja a 0.
   - **Parte 3:** reconstruir la vista y mostrar que el historial queda igual.
4. Si algo falla en vivo: Swagger en `http://localhost:8088` y `docs/evidencias/demo-salida.txt` como respaldo.

---

## 3. Quince preguntas probables y respuestas cortas

1. **¿Por qué CQRS y Event Sourcing son independientes?**
   CQRS es separar el modelo que escribe del que lee. Event Sourcing es guardar eventos en vez del estado. Se puede tener CQRS con tablas comunes (escribir en una, leer de una vista) y ES sin CQRS (leer siempre reproduciendo eventos). Dahan lo explica así: ES es una forma posible de procesar los comandos. Combinarlos tiene sentido porque los eventos alimentan naturalmente las vistas de lectura.

2. **¿Qué pasa si el proyector se cae?**
   Las escrituras siguen funcionando: el comando no depende del proyector. La vista queda atrasada. Cuando el proyector vuelve, sigue desde el checkpoint. Como el checkpoint se guarda en la misma transacción que las filas y la proyección es idempotente, no se pierde ni se duplica nada.

3. **¿Cómo se versiona un evento?**
   Nunca se editan los eventos viejos. Si cambia la forma, se crea un tipo nuevo (por ejemplo `ConductorAsignadoV2`) o un "upcaster" que al leer convierte el JSON viejo al formato nuevo. En nuestro código ese lugar es `SerializadorEventos.Deserializar`. Para corregir un hecho se agrega un evento compensatorio, no se borra.

4. **¿Para qué sirven los snapshots y por qué no los implementamos?**
   Cuando un flujo tiene miles de eventos, reproducirlos todos es lento. Un snapshot guarda "el estado en la versión N" y se reproduce desde ahí. Un viaje tiene tres o cuatro eventos, así que reproducirlos es instantáneo: sería complejidad sin beneficio.

5. **¿Por qué no usar RabbitMQ acá?**
   Para la demo el polling de la tabla es más simple, más fácil de probar y no agrega piezas que puedan fallar en vivo. RabbitMQ es la evolución natural en el TP2: publicar el evento después de guardarlo (con outbox, para no perder ninguno) y que el proyector se suscriba. Checkpoint, idempotencia y vista no cambian.

6. **¿Qué pasa con un dato personal dentro de un evento?**
   Es un problema serio: los eventos son inmutables y no se borran, así que no podríamos cumplir el derecho de supresión. Por eso solo guardamos IDs (RNF-19). Si alguna vez hiciera falta, la técnica es cifrar con una clave por persona y borrar la clave ("crypto-shredding").

7. **¿Por qué el historial devuelve 200 vacío y no 404?**
   Porque el viaje existe: la vista todavía no se enteró. Un 404 diría "no existe" y sería falso. El 200 vacío es exactamente la consistencia eventual que queremos mostrar.

8. **¿El retraso de 3 segundos es un bug?**
   No, es una característica de CQRS: la lectura se actualiza después que la escritura. En un sistema real ese retraso lo producen la cola y la carga del lector. Acá lo hicimos configurable (`Proyeccion__RetrasoMs`) para que se vea y se pueda medir (`retrasoMs`).

9. **¿Cómo evitan duplicar un evento si M5 lo reenvía?**
   Con el `eventoId` que manda el emisor, que tiene índice único. Si llega el mismo `eventoId` con el mismo contenido, devolvemos la respuesta original sin guardar nada nuevo. Si llega con otro contenido, 422.

10. **¿Qué pasa si dos módulos escriben el mismo viaje al mismo tiempo?**
    Concurrencia optimista: los dos leen la versión 1 y quieren escribir la 2. El índice único `(ViajeId, Version)` deja pasar solo a uno; el otro recibe 409 y puede reintentar. Lo probamos contra PostgreSQL real con ocho comandos simultáneos.

11. **¿Por qué `/estado` y `/historial` pueden dar respuestas distintas?**
    `/estado` lee los eventos y siempre está al día (consistencia inmediata, pero se recalcula en cada consulta). `/historial` lee la vista proyectada: es más barata y escalable, pero puede estar unos segundos atrasada.

12. **¿Cuándo NO usarían Event Sourcing o CQRS?**
    En un CRUD simple, en prototipos o donde se necesite consistencia inmediata. En M8: los comprobantes (se generan una vez y listo), el token del QR en Redis (dato efímero de un solo uso) y `/health`. Fowler advierte que para la mayoría de los sistemas CQRS agrega complejidad riesgosa y que conviene aplicarlo solo en partes puntuales.

13. **¿Qué alternativa más simple había?**
    Una tabla `Viajes` con el estado actual más una tabla de auditoría que se llena en cada cambio. Es más simple, pero la auditoría es un "extra" que puede desincronizarse; en ES el historial es la fuente de verdad. Para un módulo que solo necesita auditar, esa alternativa sería razonable: lo elegimos porque es el tema a demostrar y porque M8 ya recibe los hitos como eventos.

14. **¿Cómo se agrega una vista nueva (por ejemplo, viajes por conductor)?**
    Se escribe una proyección nueva con su propio checkpoint y se la hace reproducir todos los eventos desde el principio. No hay que migrar datos: los eventos ya tienen toda la historia. Esa es una de las ventajas de ES.

15. **¿Cómo prueban algo que depende del tiempo sin esperar?**
    Inyectamos el reloj (`TimeProvider` de .NET 8). En los tests usamos un `RelojFijo` y le decimos "ahora son 2,9 s" (no proyecta) o "ahora son 3 s" (proyecta). No hay `Thread.Sleep`.

**Preguntas extra si sobra tiempo:**
- *¿Qué es el checkpoint?* El número del último evento proyectado (`UltimaSeq`). Permite seguir donde se quedó.
- *¿Por qué 202 y no 201?* Porque aceptamos el evento pero el efecto completo (la vista, y más adelante las notificaciones) ocurre después. Lo define el contrato de M8.
- *¿Qué limitación conocida tiene el proyector?* Con muchas escrituras concurrentes, un `Seq` menor puede confirmarse después de uno mayor. El retraso lo mitiga; la solución robusta es un outbox (ver `docs/DECISIONES.md`, punto 5.9).
- *¿Hay autenticación?* No todavía: el contrato pide JWT de M1 con rol SERVICIO, y se simula en una entrega futura.
