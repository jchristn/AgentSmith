// ApiClient: request headers, normalized pages and errors (Garrison and Hugo shapes), route fallbacks, the 401
// event, and the fetch-based Server-Sent Events reader.

import { describe, it, expect, vi, afterEach } from 'vitest';
import ApiClient, { ApiError, normalizeEnumeration, parseSseEvent, parseSseFrame } from '../utils/api';

function jsonResponse(status, body, headers = {}) {
  const text = body === undefined ? '' : JSON.stringify(body);
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: 'x',
    headers: new Headers({ 'content-type': 'application/json', ...headers }),
    text: async () => text
  };
}

function sseResponse(frames) {
  const encoder = new TextEncoder();
  const stream = new ReadableStream({
    start(controller) {
      for (const f of frames) controller.enqueue(encoder.encode(f));
      controller.close();
    }
  });
  return { ok: true, status: 200, headers: new Headers({ 'content-type': 'text/event-stream' }), body: stream };
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('requests', () => {
  it('sends the bearer token, JSON content type, and Accept-Language', async () => {
    document.documentElement.lang = 'de';
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(200, { status: 'ok' }));
    const client = new ApiClient('http://127.0.0.1:8800/', 'tok');
    await client.healthCheck();
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://127.0.0.1:8800/v1.0/api/health');
    expect(init.headers.Authorization).toBe('Bearer tok');
    expect(init.headers['Accept-Language']).toBe('de');
    document.documentElement.lang = 'en';
  });

  it('uses Garrison tenant-scoped paths for identity routes and plan paths for Hugo routes', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(200, { objects: [], totalRecords: 0 }));
    const client = new ApiClient('http://h', 't');
    await client.listUsers('default', { pageNumber: 2, pageSize: 10 });
    expect(fetchMock.mock.calls[0][0]).toBe('http://h/v1.0/api/tenants/default/users?maxResults=10&skip=10');
    await client.listResources({ pageNumber: 1, pageSize: 25, connectorType: 'ssh' });
    expect(fetchMock.mock.calls[1][0]).toBe('http://h/v1.0/api/resources?pageNumber=1&pageSize=25&connectorType=ssh');
    await client.revealSecret('sec_1', 'incident');
    expect(fetchMock.mock.calls[2][0]).toBe('http://h/v1.0/api/secrets/sec_1/reveal');
    expect(JSON.parse(fetchMock.mock.calls[2][1].body)).toEqual({ reason: 'incident' });
  });

  it('falls back to the Garrison route when the Hugo route is missing', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(jsonResponse(404, { error: 'NotFound', message: 'no route' }))
      .mockResolvedValueOnce(jsonResponse(200, { version: '0.1.0-alpha' }));
    const info = await new ApiClient('http://h', 't').getServerInfo();
    expect(info.version).toBe('0.1.0-alpha');
    expect(fetchMock.mock.calls.map((c) => c[0])).toEqual(['http://h/v1.0/api/info', 'http://h/v1.0/api/server-info']);
  });

  it('raises auth:unauthorized on 401, except for token routes and secret reveals', async () => {
    const handler = vi.fn();
    window.addEventListener('auth:unauthorized', handler);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(401, { error: 'AuthenticationRequired', message: 'x' }));
    const client = new ApiClient('http://h', 't');
    await expect(client.listResources({})).rejects.toBeInstanceOf(ApiError);
    expect(handler).toHaveBeenCalledTimes(1);
    await expect(client.reauthenticate('pw')).rejects.toBeInstanceOf(ApiError);
    await expect(client.revealSecret('s', 'r')).rejects.toBeInstanceOf(ApiError);
    expect(handler).toHaveBeenCalledTimes(1);
    window.removeEventListener('auth:unauthorized', handler);
  });

  it('turns network failures into NetworkError', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'));
    await expect(new ApiClient('http://h').healthCheck()).rejects.toMatchObject({ status: 0, code: 'NetworkError' });
  });
});

describe('normalized shapes', () => {
  it('normalizes enumeration results, page results, Hugo pages, and arrays', () => {
    expect(normalizeEnumeration({ objects: [1, 2], totalRecords: 30, skip: 25, maxResults: 25 }, 2, 25)).toMatchObject({
      items: [1, 2],
      pageNumber: 2,
      pageSize: 25,
      totalCount: 30
    });
    expect(normalizeEnumeration({ items: [1], pageNumber: 3, pageSize: 10, totalCount: 21, totalPages: 3 })).toMatchObject({
      items: [1],
      pageNumber: 3,
      totalCount: 21,
      endOfResults: true
    });
    expect(normalizeEnumeration([1, 2, 3]).totalCount).toBe(3);
  });

  it('reads the Hugo error body with field details and the request ID header', () => {
    const e = new ApiError(400, {
      error: 'ValidationFailed',
      message: 'bad',
      requestId: 'req_1',
      traceId: 'tr',
      details: [{ field: 'port', code: 'OutOfRange', message: 'too big' }]
    });
    expect(e.code).toBe('ValidationFailed');
    expect(e.requestId).toBe('req_1');
    expect(e.traceId).toBe('tr');
    expect(e.fields).toEqual({ port: 'too big' });
    expect(e.fieldCodes).toEqual({ port: 'OutOfRange' });
    expect(new ApiError(500, 'oops', 'req_h').requestId).toBe('req_h');
    expect(new ApiError(403, { code: 'AuthorizationDenied', requiredPermission: 'Read' }).requiredPermission).toBe('Read');
  });
});

describe('server-sent events', () => {
  it('parses named events, ids, multi-line data, and comments', () => {
    expect(parseSseEvent('event: assistant.delta\nid: 7\ndata: {"text":"Hi"}')).toEqual({ event: 'assistant.delta', data: { text: 'Hi' }, id: '7' });
    expect(parseSseEvent('data: hello\ndata: world')).toEqual({ event: 'message', data: 'hello\nworld', id: null });
    expect(parseSseEvent(': keepalive')).toBeNull();
    expect(parseSseFrame('data: {"a":1}')).toEqual({ a: 1 });
  });

  it('streams a chat turn across chunk boundaries and CRLF line endings', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      sseResponse([
        'event: turn.started\r\ndata: {}\r\n\r\nevent: assistant.del',
        'ta\ndata: {"text":"Mr. "}\n\nevent: assistant.delta\ndata: {"text":"Anderson"}\n\n',
        'event: turn.completed\ndata: {"outputTokens":3}\n\n'
      ])
    );
    const events = [];
    await new Promise((resolve) => {
      new ApiClient('http://h', 't').streamMessage('cnv_1', { content: 'hi' }, { onEvent: (e) => events.push(e), onEnd: resolve });
    });
    expect(events.map((e) => e.event)).toEqual(['turn.started', 'assistant.delta', 'assistant.delta', 'turn.completed']);
    expect(events[1].data.text + events[2].data.text).toBe('Mr. Anderson');
  });

  it('reports a failed stream through onEnd with an ApiError', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(404, { error: 'NotFound', message: 'no route' }));
    const error = await new Promise((resolve) => new ApiClient('http://h', 't').streamEvents(() => {}, { onEnd: resolve }));
    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(404);
  });

  it('delivers a JSON (non-streaming) reply as one message event', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(200, { content: 'done' }));
    const events = [];
    await new Promise((resolve) => new ApiClient('http://h', 't').streamMessage('c', {}, { onEvent: (e) => events.push(e), onEnd: resolve }));
    expect(events).toEqual([{ event: 'message', data: { content: 'done' }, id: null }]);
  });
});
