"""Prueba de entrega de Tickets en un entorno local de M8 con Mailpit."""
import base64
import json
import os
import time
from collections import Counter
from datetime import datetime, timezone
from urllib.parse import urlencode, urlparse
from urllib.request import Request, urlopen
from uuid import uuid4


def local_url(variable, default):
    url = os.environ.get(variable, default).rstrip("/")
    if urlparse(url).hostname not in ("localhost", "127.0.0.1", "::1"):
        raise ValueError(f"{variable} debe apuntar al entorno local de prueba.")
    return url


def pedir(method, url, token=None, body=None, key=None):
    headers = {}
    if token:
        headers["Authorization"] = "Bearer " + token
    if key:
        headers["Idempotency-Key"] = key
    data = None
    if body is not None:
        headers["Content-Type"] = "application/json"
        data = json.dumps(body).encode()
    with urlopen(Request(url, data=data, headers=headers, method=method), timeout=10) as r:
        return json.load(r)


def verificar(condicion, mensaje):
    if not condicion:
        raise RuntimeError(mensaje)


def main():
    api = local_url("M8_TEST_API", "http://localhost:8088") + "/api/v1"
    mailpit = local_url("M8_TEST_MAILPIT", "http://localhost:8025")
    titular, operador, servicio = (os.environ["M8_TOKEN_" + rol] for rol in ("TITULAR", "OPERADOR", "SERVICIO"))
    # Se usa el sub solo para preparar el destinatario. M8 verifica el JWT firmado en cada solicitud.
    payload = titular.split(".")[1]
    usuario = json.loads(base64.urlsafe_b64decode(payload + "=" * (-len(payload) % 4)))["sub"]
    viaje = str(uuid4())
    email = "soporte-" + viaje + "@example.test"

    pedir("POST", api + "/eventos", servicio, {
        "eventoId": str(uuid4()), "tipo": "VIAJE_ASIGNADO", "origen": "M5",
        "ocurridoEn": datetime.now(timezone.utc).isoformat(), "viajeId": viaje,
        "destinatarios": [{"usuarioId": usuario, "rol": "CLIENTE", "email": email}],
        "detalle": {"origen": "Origen de prueba", "destino": "Destino de prueba", "tipoVehiculo": "AUTO"},
    })
    pedido = {"referencia": {"tipo": "VIAJE", "id": viaje}, "titulo": "Prueba de entrega", "descripcion": "Caso sintético."}
    clave = str(uuid4())
    alta = pedir("POST", api + "/tickets", titular, pedido, clave)
    url = api + "/tickets/" + alta["ticketId"]
    estados = ["EN_REVISION", "RESUELTO", "CERRADO"]
    resolucion = "R" * 1000
    for estado in estados:
        cambio = {"estado": estado}
        if estado == "RESUELTO":
            cambio["resolucion"] = resolucion
        pedir("PATCH", url, operador, cambio)
    verificar(pedir("POST", api + "/tickets", titular, pedido, clave) == alta, "El reintento cambió el alta.")
    verificar(pedir("GET", url, titular)["resolucion"] == resolucion, "Se perdió parte de la resolución.")
    historial = pedir("GET", url + "/historial", titular)
    verificar([c["estadoNuevo"] for c in historial["items"]] == ["ABIERTO", *estados], "Historial incorrecto.")
    metricas = pedir("GET", api + "/tickets/metricas", operador)
    verificar(metricas["cerrados"] >= 1, "El ticket cerrado no aparece en las métricas.")

    prefijo = "#" + alta["ticketId"][:8]
    esperados = Counter([f"Ticket {prefijo} recibido", f"Tu ticket {prefijo} está en revisión",
                         f"Tu ticket {prefijo} fue resuelto", f"Tu ticket {prefijo} fue cerrado"])
    busqueda = mailpit + "/api/v1/search?" + urlencode({"query": "to:" + email})
    limite = time.monotonic() + 60
    while time.monotonic() < limite:
        recibidos = pedir("GET", busqueda)["messages"] or []
        asuntos = Counter(m["Subject"] for m in recibidos if prefijo in m["Subject"] and
                          [t["Address"] for t in m["To"]] == [email])
        avisos = pedir("GET", api + "/notificaciones?referenciaId=" + viaje, operador)["items"]
        propios = [n for n in avisos if prefijo in n["asunto"]]
        if asuntos == esperados and len(propios) == 4 and all(
                n["estado"] == "ENVIADA" and n["destinatarioId"] == usuario and n["intentos"] == 1 for n in propios):
            print("OK: alta idempotente, historial, resolución de 1000 caracteres, métricas y cuatro correos en Mailpit.")
            return
        time.sleep(1)
    raise RuntimeError("No se verificaron los cuatro correos al titular y sus entregas registradas en 60 segundos.")


if __name__ == "__main__":
    main()
