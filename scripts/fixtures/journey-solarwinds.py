#!/usr/bin/env python3
"""Local SolarWinds Observability API target for the real-model primary journey."""
import argparse
import datetime
import http.server
import json
import urllib.parse


class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        if self.headers.get('Authorization') != 'Bearer journey-solarwinds-test-token':
            self.send_error(401)
            return
        path = urllib.parse.urlsplit(self.path).path
        now = datetime.datetime.now(datetime.timezone.utc).isoformat()
        page_info = {'prevPage': '', 'nextPage': ''}
        if path == '/v1/logs':
            messages = [
                'checkout request duration=1250ms database_wait=900ms upstream=prod-db',
                'checkout slow query waiting; inspect prod-db pg_stat_activity before changing anything',
                'checkout nginx upstream response duration=1210ms on web-01',
            ]
            body = {'logs': [dict(id=str(1000+i), time=now, hostname='web-01',
                                  severity='Warning', program='nginx', message=message)
                             for i, message in enumerate(messages)], 'pageInfo': page_info}
        elif path == '/v1/entities':
            body = {'entities': [dict(id='web-01', name='web-01', type='Host')], 'pageInfo': page_info}
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
