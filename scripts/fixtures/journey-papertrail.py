#!/usr/bin/env python3
"""Local Papertrail API target for the real-model primary journey; never an inference stub."""
import argparse
import datetime
import http.server
import json
import urllib.parse


class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        if self.headers.get('X-Papertrail-Token') != 'journey-papertrail-test-token':
            self.send_error(401)
            return
        path = urllib.parse.urlsplit(self.path).path
        now = datetime.datetime.now(datetime.timezone.utc).isoformat()
        if path.endswith('events/search.json'):
            messages = [
                'checkout request duration=1250ms database_wait=900ms upstream=prod-db',
                'checkout slow query waiting; inspect prod-db pg_stat_activity before changing anything',
                'checkout nginx upstream response duration=1210ms on web-01',
            ]
            events = [dict(id=str(1000+i), generated_at=now, received_at=now, source_name='web-01',
                           source_id=101, hostname='web-01', source_ip='127.0.0.1', facility='Local7',
                           severity='Warning', program='nginx', message=message) for i, message in enumerate(messages)]
            body = dict(events=events, min_id='1000', max_id='1002', reached_beginning=True, reached_record_limit=False)
        elif path.endswith('systems.json'):
            body = [dict(id=101, name='web-01', hostname='web-01', ip_address='127.0.0.1')]
        elif path.endswith('groups.json') or path.endswith('searches.json'):
            body = []
        else:
            self.send_error(404)
            return
        data = json.dumps(body).encode()
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, *_args):
        pass


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--port', type=int, required=True)
    args = parser.parse_args()
    http.server.ThreadingHTTPServer(('127.0.0.1', args.port), Handler).serve_forever()
