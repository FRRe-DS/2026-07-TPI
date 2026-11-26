import json
import subprocess
import unittest
from pathlib import Path
from unittest.mock import patch

import sampler


COMPOSE = Path('/repo/compose.yml')


def docker_output(stdout):
    return subprocess.CompletedProcess([], 0, stdout=stdout)


def values(metrics):
    return {
        (metric['name'], tuple(attr['value']['stringValue'] for attr in point['attributes'])):
            point.get('asDouble', point.get('asInt'))
        for metric in metrics for point in metric['gauge']['dataPoints']
    }


class SamplerTest(unittest.TestCase):
    @patch('subprocess.run')
    def test_exports_gauges_only_for_the_selected_project(self, run):
        run.side_effect = [
            docker_output('["m8-api-1","m8-api","running"]\n["m8-db-1","db","exited"]\n'
                          '["m8-cache-1","redis","running"]\n'),
            docker_output('["m8-api-1","1.25%","12.5MiB / 1GiB","2.5kB / 700B"]\n'
                          '["m8-cache-1","--","-- / --","0B / 3MB"]\n'
                          '["unrelated-1","99%","99MiB / 1GiB","0B / 0B"]\n'),
        ]

        metrics = values(sampler.collect(COMPOSE, 'demo', 7))

        ps, stats = (call.args[0] for call in run.call_args_list)
        self.assertEqual(ps[:8], ['docker', 'compose', '--file', str(COMPOSE),
                                  '--project-name', 'demo', 'ps', '--all'])
        self.assertEqual(stats[:3], ['docker', 'stats', '--no-stream'])
        self.assertEqual(stats[5:], ['--', 'm8-api-1', 'm8-cache-1'])
        for call in run.call_args_list:
            self.assertEqual(call.kwargs['timeout'], 7)
            self.assertEqual(call.kwargs['cwd'], COMPOSE.parent)
            self.assertEqual(call.kwargs['stderr'], subprocess.DEVNULL)
        self.assertEqual(metrics, {
            ('m8.container.running', ('m8-api', 'm8-api-1')): '1',
            ('m8.container.running', ('db', 'm8-db-1')): '0',
            ('m8.container.running', ('redis', 'm8-cache-1')): '1',
            ('m8.container.cpu.percent', ('m8-api', 'm8-api-1')): 1.25,
            ('m8.container.memory', ('m8-api', 'm8-api-1')): '13107200',
            ('m8.container.network.receive', ('m8-api', 'm8-api-1')): '2500',
            ('m8.container.network.transmit', ('m8-api', 'm8-api-1')): '700',
            ('m8.container.network.receive', ('redis', 'm8-cache-1')): '0',
            ('m8.container.network.transmit', ('redis', 'm8-cache-1')): '3000000',
        })

    @patch('subprocess.run')
    def test_stats_timeout_still_exports_running_state(self, run):
        run.side_effect = [docker_output('["api-1","api","running"]\n'),
                           subprocess.TimeoutExpired('docker stats', 7)]
        with self.assertLogs('sampler', level='WARNING'):
            metrics = values(sampler.collect(COMPOSE, None, 7))
        self.assertEqual(metrics, {('m8.container.running', ('api', 'api-1')): '1'})
        self.assertNotIn('--project-name', run.call_args_list[0].args[0])

    @patch('subprocess.run')
    def test_empty_compose_project_does_not_run_unscoped_stats(self, run):
        run.return_value = docker_output('')
        self.assertEqual(sampler.collect(COMPOSE, None, 7), [])
        run.assert_called_once()

    @patch('http.client.HTTPConnection')
    def test_export_to_loopback_and_recover_after_timeout(self, http):
        connection = http.return_value
        connection.request.side_effect = [TimeoutError, None]
        connection.getresponse.return_value.status = 200
        connection.getresponse.return_value.read.return_value = b'{}'
        metrics = [{'name': 'm8.container.running', 'gauge': {'dataPoints': []}}]

        with self.assertLogs('sampler', level='WARNING'):
            sampler.export_metrics(14318, metrics, 2)
        sampler.export_metrics(14318, metrics, 2)

        http.assert_called_with('127.0.0.1', 14318, timeout=2)
        self.assertEqual(connection.close.call_count, 2)
        method, path = connection.request.call_args.args[:2]
        self.assertEqual((method, path), ('POST', '/v1/metrics'))
        kwargs = connection.request.call_args.kwargs
        self.assertEqual(kwargs['headers']['Content-Type'], 'application/json')
        resource = json.loads(kwargs['body'])['resourceMetrics'][0]
        self.assertEqual(resource['resource']['attributes'], [
            {'key': 'service.name', 'value': {'stringValue': 'm8-docker-sampler'}},
        ])
        self.assertEqual(resource['scopeMetrics'][0]['metrics'], metrics)
