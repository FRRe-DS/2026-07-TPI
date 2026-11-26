"""Prueba End-to-End de Notificaciones (M8, TP3, RNF-07) contra el entorno local levantado con docker compose.

Recorre el flujo completo con los servicios reales (API, PostgreSQL, RabbitMQ, Redis y Mailpit) y el
Keycloak de la cátedra:

  1. GET /health responde 200 y lleva los encabezados de seguridad (RNF-12).
  2. VIAJE_ASIGNADO por HTTP → 202; el mismo evento otra vez → misma respuesta (RNF-09).
  3. RESERVA_CONFIRMADA publicado en RabbitMQ → la cola lo consume (RF-8.8, RNF-10).
  4. Los dos avisos al cliente quedan ENVIADA y los correos llegan a Mailpit (RF-8.1, RF-8.2).
  5. Un aviso a un contacto que falla hace 3 intentos y queda FALLIDA; el reintento del OPERADOR
     agrega exactamente un intento más y la misma Idempotency-Key devuelve la respuesta original (RF-8.6).
  6. Ninguna respuesta de la API expone el email del destinatario (RNF-19).

Tokens (uno de los dos):
  - M8_TOKEN_SERVICIO y M8_TOKEN_OPERADOR, ya obtenidos; o
  - KEYCLOAK_CLIENT_SECRET del client grupo-07 (en local actúa como SERVICIO y OPERADOR; ver docker-compose.yml).
Opcionales: M8_TEST_API (http://localhost:8088), M8_TEST_MAILPIT (http://localhost:8025),
M8_TEST_RABBITMQ_API (http://localhost:15672), RABBITMQ_USER/RABBITMQ_PASSWORD (los del .env),
KEYCLOAK_URL (http://localhost:8080). Solo acepta URLs locales: no es para correr contra un entorno publicado.

Uso: python scripts/e2e-notificaciones.py      (sale con código 0 si todo pasa)
"""
import base64
import json
import os
import sys
import time
from datetime import datetime, timezone
from urllib.error import HTTPError
from urllib.parse import urlencode, urlparse
from urllib.request import Request, urlopen
from uuid import uuid4

REALM = "ds-2026-realm"
CLIENTE_KEYCLOAK = "grupo-07"
# 3 intentos con esperas de 5 s y 30 s, más el ciclo del despachador: ~40 s. Margen amplio.
ESPERA_FALLIDA_S = 90


def local_url(variable, default):
    url = os.environ.get(variable, default).rstrip("/")
    if urlparse(url).hostname not in ("localhost", "127.0.0.1", "::1"):
        raise ValueError(f"{variable} debe apuntar al entorno local de prueba.")
    return url


def pedir(method, url, token=None, body=None, key=None, basic=None, form=None, request_id=None):
    """Devuelve (status, headers, cuerpo JSON o None). No lanza por códigos 4xx/5xx."""
    headers = {}
    data = None
    if token:
        headers["Authorization"] = "Bearer " + token
    if basic:
        headers["Authorization"] = "Basic " + base64.b64encode(f"{basic[0]}:{basic[1]}".encode()).decode()
    if key:
        headers["Idempotency-Key"] = key
    if request_id:
        headers["X-Request-Id"] = request_id
    if form is not None:
        headers["Content-Type"] = "application/x-www-form-urlencoded"
        data = urlencode(form).encode()
    elif body is not None:
        headers["Content-Type"] = "application/json"
        data = json.dumps(body).encode()
    try:
        with urlopen(Request(url, data=data, headers=headers, method=method), timeout=15) as r:
            crudo = r.read()
            return r.status, r.headers, (json.loads(crudo) if crudo else None), crudo
    except HTTPError as e:
        crudo = e.read()
        try:
            cuerpo = json.loads(crudo) if crudo else None
        except ValueError:
            cuerpo = None
        return e.code, e.headers, cuerpo, crudo


def verificar(condicion, mensaje):
    if not condicion:
        raise AssertionError(mensaje)
    print("  ok -", mensaje)


def esperar(descripcion, condicion, limite_s):
    fin = time.monotonic() + limite_s
    while time.monotonic() < fin:
        resultado = condicion()
        if resultado:
            return resultado
        time.sleep(1)
    raise AssertionError(f"No se cumplió en {limite_s} s: {descripcion}")


def tokens():
    servicio, operador = os.environ.get("M8_TOKEN_SERVICIO"), os.environ.get("M8_TOKEN_OPERADOR")
    if servicio and operador:
        return servicio, operador
    secreto = os.environ.get("KEYCLOAK_CLIENT_SECRET")
    if not secreto:
        raise SystemExit("Definí M8_TOKEN_SERVICIO y M8_TOKEN_OPERADOR, o KEYCLOAK_CLIENT_SECRET.")
    keycloak = local_url("KEYCLOAK_URL", "http://localhost:8080")
    status, _, cuerpo, _ = pedir("POST", f"{keycloak}/realms/{REALM}/protocol/openid-connect/token", form={
        "grant_type": "client_credentials", "client_id": CLIENTE_KEYCLOAK, "client_secret": secreto})
    if status != 200:
        raise SystemExit(f"Keycloak respondió {status} al pedir el token.")
    return cuerpo["access_token"], cuerpo["access_token"]


def evento(tipo, origen, destinatarios, **ids):
    cuerpo = {"eventoId": str(uuid4()), "tipo": tipo, "origen": origen,
              "ocurridoEn": datetime.now(timezone.utc).isoformat(), "destinatarios": destinatarios,
              "detalle": {"origen": "Av. Sarmiento 100", "destino": "Plaza 25 de Mayo", "tipoVehiculo": "AUTO"}}
    cuerpo.update({k: str(v) for k, v in ids.items()})
    return cuerpo


def main():
    # La consola de Windows usa cp1252 por defecto y no imprime "→".
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    api = local_url("M8_TEST_API", "http://localhost:8088")
    v1 = api + "/api/v1"
    mailpit = local_url("M8_TEST_MAILPIT", "http://localhost:8025")
    rabbit = local_url("M8_TEST_RABBITMQ_API", "http://localhost:15672")
    rabbit_cred = (os.environ.get("RABBITMQ_USER", "guest"), os.environ.get("RABBITMQ_PASSWORD", "guest"))
    servicio, operador = tokens()

    corrida = uuid4().hex[:8]
    cliente, conductor, fallido = uuid4(), uuid4(), uuid4()
    email = f"e2e-{corrida}@example.test"
    email_falla = f"e2e-{corrida}@falla.test"  # el proveedor de prueba rechaza todo contacto con "falla"
    viaje, reserva, viaje_falla = uuid4(), uuid4(), uuid4()

    print("1. Salud y encabezados de seguridad")
    status, headers, cuerpo, _ = pedir("GET", api + "/health")
    verificar(status == 200 and cuerpo == {"estado": "ok"}, "GET /health → 200 {estado: ok}")
    verificar(headers.get("X-Content-Type-Options") == "nosniff" and headers.get("X-Frame-Options") == "DENY",
              "encabezados de seguridad presentes")
    status, headers, cuerpo, _ = pedir("GET", v1 + "/notificaciones")
    verificar(status == 401 and headers.get("X-Request-Id"), "sin token → 401 con X-Request-Id")

    print("2. Evento de viaje por HTTP, con repetición idempotente")
    asignado = evento("VIAJE_ASIGNADO", "M5", [
        {"usuarioId": str(cliente), "rol": "CLIENTE", "email": email, "canalesPreferidos": ["EMAIL"]},
        {"usuarioId": str(conductor), "rol": "CONDUCTOR", "telefono": "+5493624000000"}], viajeId=viaje)
    s1, _, r1, _ = pedir("POST", v1 + "/eventos", servicio, asignado, request_id=f"e2e-{corrida}-asignado")
    s2, _, r2, _ = pedir("POST", v1 + "/eventos", servicio, asignado)
    verificar(s1 == 202 and s2 == 202 and r1 == r2, "202 y la repetición devuelve la respuesta original")
    otro = dict(asignado, tipo="VIAJE_INICIADO", origen="M6")
    verificar(pedir("POST", v1 + "/eventos", servicio, otro)[0] == 422, "mismo eventoId con otro cuerpo → 422")

    print("3. Evento de reserva por RabbitMQ")
    confirmada = evento("RESERVA_CONFIRMADA", "M9", [
        {"usuarioId": str(cliente), "rol": "CLIENTE", "email": email}], reservaId=reserva)
    status, _, cuerpo, _ = pedir("POST", rabbit + "/api/exchanges/%2F/movilidad.eventos/publish", basic=rabbit_cred, body={
        "routing_key": "reserva.confirmada", "payload": json.dumps(confirmada), "payload_encoding": "string",
        "properties": {"content_type": "application/json", "delivery_mode": 2, "app_id": "e2e-m9",
                       "correlation_id": f"e2e-{corrida}-reserva"}})
    verificar(status == 200 and cuerpo.get("routed"), "publicado en movilidad.eventos y enrutado a m8.eventos")

    print("4. Entrega real: notificaciones ENVIADA y correos en Mailpit")

    def avisos_de(referencia):
        return pedir("GET", f"{v1}/notificaciones?{urlencode({'referenciaId': referencia})}", operador)[2]["items"]

    def enviados():
        viaje_ok = [n for n in avisos_de(viaje) if n["estado"] == "ENVIADA"]
        reserva_ok = [n for n in avisos_de(reserva) if n["estado"] == "ENVIADA"]
        return (viaje_ok, reserva_ok) if len(viaje_ok) == 2 and len(reserva_ok) == 1 else None

    viaje_ok, reserva_ok = esperar("los 3 avisos ENVIADA", enviados, 60)
    verificar({n["canal"] for n in viaje_ok} == {"EMAIL", "SMS"}, "viaje: cliente por EMAIL y conductor por SMS")
    verificar(reserva_ok[0]["tipoEvento"] == "RESERVA_CONFIRMADA", "reserva consumida desde la cola y enviada")

    def correos():
        mensajes = pedir("GET", mailpit + "/api/v1/search?" + urlencode({"query": "to:" + email}))[2]["messages"] or []
        return mensajes if len(mensajes) >= 2 else None

    verificar(len(esperar("2 correos en Mailpit", correos, 30)) == 2, "los 2 correos al cliente llegaron a Mailpit")

    print("5. Reintentos automáticos, FALLIDA y reintento del OPERADOR")
    pedir("POST", v1 + "/eventos", servicio, evento("VIAJE_ASIGNADO", "M5", [
        {"usuarioId": str(fallido), "rol": "CLIENTE", "email": email_falla, "canalesPreferidos": ["EMAIL"]}],
        viajeId=viaje_falla))

    def fallida():
        items = avisos_de(viaje_falla)
        return items[0] if items and items[0]["estado"] == "FALLIDA" else None

    n = esperar("el aviso al contacto que falla queda FALLIDA", fallida, ESPERA_FALLIDA_S)
    verificar(n["intentos"] == 3, "3 intentos automáticos con espera creciente")
    url = f"{v1}/notificaciones/{n['notificacionId']}"
    clave = f"e2e-{corrida}-reintento"
    s1, _, r1, _ = pedir("POST", url + "/reintentos", operador, key=clave)
    s2, _, r2, _ = pedir("POST", url + "/reintentos", operador, key=clave)
    verificar(s1 == 202 and r1["estado"] == "PENDIENTE", "reintento → 202 PENDIENTE")
    verificar(s2 == 202 and r2 == r1, "misma Idempotency-Key → respuesta original")
    final = esperar("cuarto intento registrado",
                    lambda: (lambda x: x if x["estado"] == "FALLIDA" and x["intentos"] == 4 else None)(
                        pedir("GET", url, operador)[2]), 30)
    intentos = pedir("GET", url + "/intentos", operador)[2]
    verificar(final["intentos"] == 4 and intentos["totalElementos"] == 4, "exactamente un intento más, historial de 4")
    verificar(pedir("POST", url + "/reintentos", operador, key=clave + "-b")[0] == 202,
              "una FALLIDA se puede volver a reintentar")
    enviada = viaje_ok[0]["notificacionId"]
    verificar(pedir("POST", f"{v1}/notificaciones/{enviada}/reintentos", operador, key=clave + "-c")[0] == 409,
              "reintentar una ENVIADA → 409")

    print("6. Privacidad")
    _, _, _, crudo = pedir("GET", f"{v1}/notificaciones?size=100", operador)
    verificar(email.encode() not in crudo and email_falla.encode() not in crudo, "la API no expone emails de destinatarios")

    print("\nOK: flujo E2E de Notificaciones completo (HTTP, RabbitMQ, envío real, reintentos y privacidad).")


if __name__ == "__main__":
    try:
        main()
    except AssertionError as error:
        print("\nFALLÓ:", error, file=sys.stderr)
        sys.exit(1)
