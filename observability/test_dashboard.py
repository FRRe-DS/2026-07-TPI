import json
import unittest
from pathlib import Path


DASHBOARD = Path(__file__).with_name('m8-dashboard.json')


class DashboardTest(unittest.TestCase):
    def test_queries_use_provisioned_datasource_and_counter_contract(self):
        panels = json.loads(DASHBOARD.read_text(encoding='utf-8'))['panels']
        self.assertEqual(len(panels), len({panel['id'] for panel in panels}))
        for panel in panels:
            self.assertEqual(panel['datasource'], {'type': 'prometheus', 'uid': 'prometheus'})
        for panel_id, metric, label in (
            (1, 'm8_notificaciones_finalizadas_total', 'resultado="enviada"'),
            (2, 'm8_notificaciones_finalizadas_total', 'reintentada="si"'),
            (3, 'm8_qr_finalizadas_total', 'resultado="generado"'),
            (5, 'http_server_request_duration_seconds_count', 'http_response_status_code=~"5.."'),
        ):
            target, = next(panel['targets'] for panel in panels if panel['id'] == panel_id)
            self.assertTrue(target['instant'])
            denominator = f'sum(increase({metric}{{job="m8-api"}}[$__range]))'
            self.assertEqual(target['expr'],
                             f'100 * (sum(increase({metric}{{job="m8-api",{label}}}[$__range])) '
                             f'or (0 * {denominator})) / ({denominator} > 0)')

    def test_latency_uses_histogram_buckets_and_allows_for_minute_exports(self):
        panels = json.loads(DASHBOARD.read_text(encoding='utf-8'))['panels']
        targets = next(panel['targets'] for panel in panels if panel['id'] == 6)
        self.assertEqual([target['legendFormat'] for target in targets], ['p50', 'p95', 'p99'])
        for target, q in zip(targets, ('0.50', '0.95', '0.99')):
            self.assertEqual(target['expr'], f'histogram_quantile({q}, sum by (le) (rate('
                             'http_server_request_duration_seconds_bucket{job="m8-api"}'
                             '[$__rate_interval])))')
            self.assertEqual(target['interval'], '60s')
