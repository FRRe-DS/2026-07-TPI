"""Muestreador Docker del proyecto Compose del módulo M8 (Python 3, sin dependencias)."""

import argparse
import http.client
import json
import logging
import math
import os
import re
import subprocess
import time
from decimal import Decimal
from pathlib import Path


LOG = logging.getLogger('sampler')
DEFAULT_COMPOSE = Path(__file__).resolve().parent.parent / 'docker-compose.yml'
BYTE_FACTORS = {'B': 1, **{f'{p}B': 1000 ** i for i, p in enumerate('KMGTPE', 1)},
                **{f'{p}IB': 1024 ** i for i, p in enumerate('KMGTPE', 1)}}
PS_FORMAT = '[{{json .Name}},{{json .Service}},{{json .State}}]'
STATS_FORMAT = '[{{json .Name}},{{json .CPUPerc}},{{json .MemUsage}},{{json .NetIO}}]'


def docker_rows(args, cwd, timeout, width):
    try:
        result = subprocess.run(['docker', *args], cwd=cwd, timeout=timeout, check=True,
                                stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
    except (OSError, subprocess.SubprocessError):
        # No exponer el stderr de Compose ni registrar la línea de comando.
        LOG.warning('No se pudo completar docker %s; se volverá a muestrear.', args[0])
        return []
    rows = []
    for line in result.stdout.splitlines():
        try:
            row = json.loads(line)
        except ValueError:
            continue
        if isinstance(row, list) and len(row) == width and all(isinstance(v, str) for v in row):
            rows.append(row)
    return rows


def bytes_value(text):
    match = re.fullmatch(r'\s*(\d+(?:\.\d+)?)\s*([a-zA-Z]+)\s*', text)
    if match and (factor := BYTE_FACTORS.get(match[2].upper())):
        size = int(Decimal(match[1]) * factor)
        if size < 2**63:
            return size
    return None


def cpu_value(text):
    if text.endswith('%'):
        try:
            cpu = float(text[:-1])
            if math.isfinite(cpu) and cpu >= 0:
                return cpu
        except ValueError:
            pass
    return None


def attr(key, value):
    return {'key': key, 'value': {'stringValue': value}}


def collect(compose_file, project_name, timeout):
    compose = ['compose', '--file', str(compose_file)]
    if project_name:
        compose.extend(['--project-name', project_name])
    rows = docker_rows([*compose, 'ps', '--all', '--format', PS_FORMAT], compose_file.parent, timeout, 3)
    containers = {name: (service, state) for name, service, state in rows if name and service and state}
    running = [name for name, (_, state) in containers.items() if state == 'running']
    stats = docker_rows(['stats', '--no-stream', '--format', STATS_FORMAT, '--', *running],
                        compose_file.parent, timeout, 4) if running else []
    timestamp = str(time.time_ns())
    metrics = {}

    def point(metric_name, name, value, unit=''):
        if value is None:
            return
        metric = metrics.setdefault(metric_name, {
            'name': f'm8.container.{metric_name}', 'unit': unit, 'gauge': {'dataPoints': []},
        })
        number = {'asInt': str(value)} if isinstance(value, int) else {'asDouble': value}
        metric['gauge']['dataPoints'].append({
            'attributes': [attr('service', containers[name][0]), attr('container', name)],
            'timeUnixNano': timestamp, **number,
        })

    for name, (_, state) in containers.items():
        point('running', name, int(state == 'running'))
    for name, cpu, mem, net in stats:
        if name not in running:
            continue
        rx, _, tx = net.partition('/')
        point('cpu.percent', name, cpu_value(cpu))
        point('memory', name, bytes_value(mem.partition('/')[0]), 'By')
        point('network.receive', name, bytes_value(rx), 'By')
        point('network.transmit', name, bytes_value(tx), 'By')
    return list(metrics.values())


def export_metrics(port, metrics, timeout):
    payload = {'resourceMetrics': [{
        'resource': {'attributes': [attr('service.name', 'm8-docker-sampler')]},
        'scopeMetrics': [{'scope': {'name': 'm8.docker'}, 'metrics': metrics}],
    }]}
    connection = http.client.HTTPConnection('127.0.0.1', port, timeout=timeout)
    try:
        connection.request('POST', '/v1/metrics', body=json.dumps(payload),
                           headers={'Content-Type': 'application/json'})
        response = connection.getresponse()
        if response.status != 200:
            LOG.warning('Colector OTLP respondió HTTP %s; se descarta la muestra.', response.status)
        elif int(json.loads(response.read(4096) or b'{}').get('partialSuccess', {})
                 .get('rejectedDataPoints', 0)):
            LOG.warning('Colector OTLP rechazó puntos de la muestra.')
    except (OSError, http.client.HTTPException, ValueError, AttributeError, TypeError):
        LOG.warning('Sin confirmación OTLP; se continuará con la siguiente muestra.')
    finally:
        connection.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--compose-file', type=Path, default=DEFAULT_COMPOSE)
    parser.add_argument('--project-name', help='mismo proyecto que docker compose -p')
    parser.add_argument('--port', type=int, default=os.getenv('M8_OBS_OTLP_HTTP_PORT') or '4318')
    parser.add_argument('--interval', type=float, default=15, help='segundos entre muestreos')
    parser.add_argument('--timeout', type=float, default=5, help='segundos por comando Docker o socket OTLP')
    parser.add_argument('--once', action='store_true', help='muestrear una vez y salir')
    args = parser.parse_args()
    if not 1 <= args.port <= 65535:
        parser.error('--port debe estar entre 1 y 65535')
    if not all(math.isfinite(n) and n > 0 for n in (args.interval, args.timeout)):
        parser.error('--interval y --timeout deben ser finitos y mayores a cero')
    compose_file = args.compose_file.resolve()
    if not compose_file.is_file():
        parser.error('no se encontró el archivo Compose indicado')
    logging.basicConfig(format='%(asctime)s %(levelname)s %(message)s', level=logging.WARNING)
    try:
        while True:
            metrics = collect(compose_file, args.project_name, args.timeout)
            if metrics:
                export_metrics(args.port, metrics, args.timeout)
            if args.once:
                return
            time.sleep(args.interval)
    except KeyboardInterrupt:
        return


if __name__ == '__main__':
    main()
