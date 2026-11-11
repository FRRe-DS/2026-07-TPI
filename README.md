# Trabajo Práctico Integrador (TPI) - Grupo 07

**Módulo Asignado:** M8 - Notificaciones, Documentos y Soporte  
**Cátedra:** Diseño de Sistemas - UTN FRRe  
**Año:** 2026  

---

## 👥 Integrantes

| Legajo | Apellido y Nombre | Usuario de GitHub | Correo Electrónico |
|:------:|:------------------|:------------------|:-------------------|
| 27909  | Pérez Tourn, Adonai | [@Adonai1027](https://github.com/Adonai1027) | adonaipe80@gmail.com |
| 27930  | Berecoechea, Lucas | [@LucBere](https://github.com/LucBere) | lucasberecoechea2@gmail.com |
| 27931  | Del Vescovo, Benno | [@bennodelvescovo](https://github.com/bennodelvescovo) | delvescovobenno@gmail.com |
| 28219  | Leguizamón, Francisco | [@franciscoleguizamon2808](https://github.com/franciscoleguizamon2808) | franciscot.leguizamon@gmail.com |
| 28289  | Perdomo Flores, Luis Alejandro | [@Alemorii](https://github.com/Alemorii) | alejoaadigi@gmail.com |
| 28476  | Galfrascoli, Ángel | [@Anfras](https://github.com/Anfras) | angelgalfras@gmail.com |
| 28480  | Rister, Matías Ezequiel | [@matiasristet02](https://github.com/matiasristet02) | matiasezerister@gmail.com |
| 28889  | Bianciotto, Benjamín | [@Benjaminbainciotto](https://github.com/Benjaminbainciotto) | Benjaminbianciottofacultad@gmail.com |
| 29777  | Cerdan, David Gabriel Esteban | [@DabiZx](https://github.com/DabiZx) | davidgabriel_0306@hotmail.com |

---

## 🛠️ Tecnologías y Lenguajes Elegidos

- **Lenguaje Principal:** C# (.NET 8)
- **Framework Backend:** ASP.NET Core Web API
- **Documentación de API:** OpenAPI 3.0 / Swagger
- **Contenedores:** Docker / Docker Compose
- **Mensajería Asíncrona:** RabbitMQ
- **Base de Datos / Persistencia:** PostgreSQL

---

## 📌 Alcance del Módulo (M8)

El módulo **M8 - Notificaciones, Documentos y Soporte** se encarga de:
- Consumo asíncrono de eventos de viaje (M5, M6, M7, M9) para envío de notificaciones (push, email, SMS).
- Generación y validación de códigos QR temporales de un solo uso para inicio de viaje.
- Generación y reenvío de comprobantes de pago en PDF.
- Registro y gestión de tickets de soporte asociados a viajes o reservas.

---

## ▶️ Cómo levantar el módulo

Requisitos: Docker y Docker Compose.

1. Copiar las variables de entorno y completarlas (el archivo `.env` no se sube al repo, RNF-06):
   ```bash
   cp .env.example .env
   ```
   Completar al menos `POSTGRES_PASSWORD`, `RABBITMQ_USER` y `RABBITMQ_PASSWORD`.
2. Levantar todo:
   ```bash
   docker compose up --build
   ```
3. Abrir Swagger en <http://localhost:8088> (M8 usa el puerto 8088, por la convención 8080 + número de módulo).

Para correr la API fuera de Docker (`dotnet run`), definir la cadena de conexión por variable de entorno, con los mismos valores del `.env` y el puerto de Postgres publicado:

```bash
export ConnectionStrings__PostgresConnection="Host=localhost;Port=5433;Database=m8_soporte_db;Username=postgres;Password=<tu_password>"
```

## 🧪 Pruebas

```bash
dotnet test M8.Notificaciones.sln
```

Los tests corren también en cada push y pull request (GitHub Actions, `.github/workflows/ci.yml`).

### Cobertura

La cobertura se mide con [coverlet](https://github.com/coverlet-coverage/coverlet) (`coverlet.collector`) y el reporte se arma con [ReportGenerator](https://github.com/danielpalme/ReportGenerator), instalado como herramienta local del repo:

```bash
dotnet tool restore
dotnet test M8.Notificaciones.sln --collect:"XPlat Code Coverage" --results-directory ./coverage
dotnet reportgenerator -reports:"./coverage/**/coverage.cobertura.xml" -targetdir:./coverage/report -reporttypes:"Html;TextSummary"
```

El resumen queda en `coverage/report/Summary.txt` y el detalle línea por línea en `coverage/report/index.html`.

Para ver solo un módulo, filtrar por namespace al generar el reporte. Por ejemplo, Notificaciones:

```bash
dotnet reportgenerator -reports:"./coverage/**/coverage.cobertura.xml" -targetdir:./coverage/report-notificaciones -reporttypes:"Html;TextSummary" -classfilters:"+M8.Notificaciones.Api.Features.Notificaciones.*"
```

**Qué se excluye y por qué:** las migraciones de EF Core y su `M8DbContextModelSnapshot` (namespace `M8.Notificaciones.Api.Infrastructure.Persistence.Migrations`). Las genera `dotnet ef migrations add` a partir del modelo y no tienen lógica propia, así que no tiene sentido escribirles tests. Si se cuentan, bajan el porcentaje total sin decir nada sobre la calidad del código del módulo. La exclusión está en `tests/M8.Notificaciones.Tests/coverlet.runsettings`, que el proyecto de tests carga solo (`RunSettingsFilePath`), así que aplica a cualquier `dotnet test --collect`.

## 🔁 CQRS + Event Sourcing (demo)

Demo de investigación del Grupo 7 (temas 21 *Event Sourcing* y 13 *CQRS*) sobre un único escenario: el **historial de viajes**. Los hitos que M5 y M6 informan por `POST /api/v1/eventos` (`VIAJE_SOLICITADO`, `VIAJE_ASIGNADO`, `VIAJE_FINALIZADO`) se guardan como **eventos inmutables** (Event Sourcing). Un proyector asíncrono arma una **vista de lectura** con un retraso visible (CQRS).

- Código: `src/M8.Notificaciones.Api/Features/Viajes/` (`Dominio/`, `EventStore/`, `Comandos/`, `Consultas/`, `Proyecciones/`, `Controllers/`, `Dtos/`).
- Decisiones y diferencias con el contrato: [`docs/DECISIONES.md`](docs/DECISIONES.md).
- Guía para el coloquio: [`docs/GUIA_DE_EXPLICACION.md`](docs/GUIA_DE_EXPLICACION.md).
- Salidas reales de la demo, los tests y la cobertura: [`docs/evidencias/`](docs/evidencias/).

> **Autenticación:** todavía no está implementada. El contrato pide JWT de M1 (rol SERVICIO); se simula en una entrega futura.

### Endpoints

| Lado | Método y ruta | Qué hace |
|------|---------------|----------|
| Escritura (ES) | `POST /api/v1/eventos` | Registra un hito como evento. `202`; mismo `eventoId` y mismo cuerpo → respuesta original; otro cuerpo → `422`; transición inválida o concurrencia → `409`. |
| Lectura ES | `GET /api/v1/viajes/{viajeId}/estado` | Estado reconstruido reproduciendo los eventos. `404` si no hay eventos. |
| Lectura ES | `GET /api/v1/viajes/{viajeId}/eventos` | Flujo crudo de eventos, en orden. |
| Lectura CQRS | `GET /api/v1/viajes/{viajeId}/historial` | Lee solo la vista proyectada. `200` con `items: []` si todavía no se proyectó. |
| Laboratorio | `GET /api/v1/lab/proyeccion/estado` | Eventos en el store, proyectados y pendientes. **Fuera del contrato.** |
| Laboratorio | `POST /api/v1/lab/proyeccion/reconstruir` | Borra la vista y la rearma desde los eventos. **Fuera del contrato.** |

Ejemplo de cuerpo (`detalle.conductorId` es una extensión nuestra y `destinatarios` es opcional; ver `docs/DECISIONES.md`):

```json
{ "eventoId": "0b8c...", "tipo": "VIAJE_ASIGNADO", "origen": "M5", "ocurridoEn": "2026-11-11T13:00:00Z",
  "viajeId": "5f2a...", "detalle": { "conductorId": "9e41..." } }
```

### Instalación y ejecución

1. Copiar `.env.example` a `.env` y completarlo, como en [Cómo levantar el módulo](#️-cómo-levantar-el-módulo). El retraso de la proyección se ajusta con `Proyeccion__RetrasoMs` (por defecto `3000`).
2. Levantar todo. La migración `AgregarViajesEventSourcing` se aplica sola al arrancar:
   ```powershell
   docker compose up --build
   ```
3. En otra terminal de PowerShell (Windows PowerShell 5.1 o PowerShell 7), correr la demo:
   ```powershell
   .\scripts\demo-cqrs-es.ps1                      # pausa con Enter entre partes
   .\scripts\demo-cqrs-es.ps1 -BaseUrl http://localhost:8088 -SinPausa
   ```
   Si la política de ejecución lo bloquea: `powershell -ExecutionPolicy Bypass -File .\scripts\demo-cqrs-es.ps1`.

### Tests y cobertura de la feature

```powershell
dotnet test M8.Notificaciones.sln
```

Las dos pruebas de integración contra PostgreSQL real se **omiten** salvo que se defina `M8_TEST_POSTGRES`. Con el Postgres del compose levantado (usar los valores de tu `.env`):

```powershell
$env:M8_TEST_POSTGRES = "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=<tu_password>"
dotnet test M8.Notificaciones.sln
```

Cada prueba crea una base temporal, aplica las migraciones y la borra al terminar.

Cobertura solo de `Features/Viajes`, con las mismas herramientas de la sección [Cobertura](#cobertura):

```powershell
dotnet tool restore
dotnet test M8.Notificaciones.sln --collect:"XPlat Code Coverage" --results-directory ./coverage
dotnet reportgenerator -reports:"./coverage/**/coverage.cobertura.xml" -targetdir:./coverage/report-viajes -reporttypes:"Html;TextSummary" -classfilters:"+M8.Notificaciones.Api.Features.Viajes.*"
```

Para regenerar la migración se usa `dotnet-ef` como herramienta local (`dotnet tool restore` y después `dotnet dotnet-ef ...`).

## 📄 Contrato de la API

El contrato OpenAPI de M8 está en [`docs/m8-openapi.yaml`](docs/m8-openapi.yaml).

