# Soporte y Tickets · RF-8.7

La API usa el contrato de Tickets de [M8](m8-openapi.yaml). Un CLIENTE o CONDUCTOR abre y consulta sus tickets de viaje, reserva o pago. El OPERADOR puede consultar todos y gestionar los cambios de estado. El titular se obtiene del `sub` de su JWT. La tabla de historia y los avisos se guardan junto al ticket en la misma transacción. El despachador hace el envío después de confirmar el cambio.

## Métricas de operación

La extensión `GET /api/v1/tickets/metricas` requiere OPERADOR. Los contadores representan el estado actual. El tiempo medio de resolución mide minutos desde la creación hasta la **primera** transición a RESUELTO de cada ticket. Una reapertura conserva ese primer tiempo. Cada ticket aporta una muestra y los que nunca se resolvieron quedan fuera del promedio. Si no hay muestras se devuelve 0. Por ejemplo, dos tickets resueltos por primera vez a los 30 y 90 minutos dan un promedio de 60, aunque cierren días después.

## Comprobar la entrega de avisos

En un entorno de prueba local, iniciar M8 con PostgreSQL, el despachador habilitado y correo SMTP dirigido al buzón de Mailpit del Compose. Usar un titular exclusivo de prueba: el evento sintético registra un contacto de prueba para ese usuario. Preparar tres tokens vigentes del emisor configurado en M8, uno por rol. El proceso lee los tokens desde estas variables de entorno:

| Variable | Permiso |
| --- | --- |
| `M8_TOKEN_TITULAR` | CLIENTE para crear el ticket y leer su historial |
| `M8_TOKEN_OPERADOR` | OPERADOR para cambiar estados y consultar la entrega |
| `M8_TOKEN_SERVICIO` | SERVICIO para ingresar un evento sintético de M5 con el contacto del titular |

Ejecutar desde la raíz con Python 3:

```sh
python scripts/verificar-soporte.py
```

La API predeterminada es `http://localhost:8088` y la bandeja `http://localhost:8025`. Se pueden cambiar con `M8_TEST_API` y `M8_TEST_MAILPIT`. El script crea un viaje de referencia y un destinatario de prueba únicos, abre un ticket, lo pasa por revisión, resolución y cierre y repite el alta con la misma clave. Comprueba el historial, una resolución del largo máximo y el estado ENVIADA. Lee los cuatro correos en Mailpit y verifica sus asuntos y su destinatario. Las pruebas automáticas cubren además reapertura, aislamiento entre titulares y concurrencia.

## Identidad para la integración del grupo

M1 emite las credenciales. M8 valida el JWT con los metadatos OIDC del emisor y aplica los roles en Tickets. El [realm publicado por la cátedra](https://github.com/FRRe-DS/2026-TPI/tree/main/keycloak) configura cuentas de servicio con roles técnicos. Para probar usuarios finales en el despliegue compartido, el integrador debe proporcionar identidades CLIENTE/CONDUCTOR y OPERADOR o confirmar la equivalencia oficial de sus claims. La verificación local puede usar un realm aislado con esos roles y un emisor configurado externamente.
