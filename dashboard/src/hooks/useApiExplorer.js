import { useState, useEffect, useMemo, useCallback, useRef } from 'react';
import {
  flattenOpenApiSpec,
  groupOperations,
  getParameterDefault,
  getRequestBodyTemplate,
  resolvePath,
  buildUrl
} from '../utils/openApi';
import { STORAGE_KEYS } from '../utils/constants';

const HISTORY_LIMIT = 12;

function loadHistory() {
  try {
    return JSON.parse(localStorage.getItem(STORAGE_KEYS.apiExplorerHistory) || '[]');
  } catch {
    return [];
  }
}

/** State + execution for the OpenAPI-driven API Explorer. */
export default function useApiExplorer(apiClient) {
  const [spec, setSpec] = useState(null);
  const [specError, setSpecError] = useState(null);
  const [loadingSpec, setLoadingSpec] = useState(true);
  const [operationId, setOperationId] = useState(null);
  const [pathParams, setPathParams] = useState({});
  const [queryParams, setQueryParams] = useState({});
  const [headers, setHeaders] = useState({ Accept: 'application/json' });
  const [body, setBody] = useState('');
  const [response, setResponse] = useState(null);
  const [running, setRunning] = useState(false);
  const [history, setHistory] = useState(loadHistory);
  const [streaming, setStreaming] = useState(false);
  const abortRef = useRef(null);
  const skipSeedRef = useRef(false);

  useEffect(() => {
    let active = true;
    setLoadingSpec(true);
    apiClient
      .getOpenApiSpec()
      .then((s) => {
        if (!active) return;
        if (!s || !s.paths) throw new Error('no paths');
        setSpec(s);
        setSpecError(null);
      })
      .catch((e) => active && setSpecError(e.message || 'error'))
      .finally(() => active && setLoadingSpec(false));
    return () => {
      active = false;
    };
  }, [apiClient]);

  const operations = useMemo(() => flattenOpenApiSpec(spec), [spec]);
  const grouped = useMemo(() => groupOperations(operations), [operations]);
  const operation = useMemo(() => operations.find((o) => o.id === operationId) || null, [operations, operationId]);

  // Seed forms when the operation changes.
  useEffect(() => {
    if (!operation) return;
    if (skipSeedRef.current) {
      skipSeedRef.current = false;
      return;
    }
    const pp = {};
    const qp = {};
    for (const p of operation.parameters) {
      if (p.in === 'path') pp[p.name] = getParameterDefault(p);
      else if (p.in === 'query') qp[p.name] = getParameterDefault(p);
    }
    setPathParams(pp);
    setQueryParams(qp);
    setBody(getRequestBodyTemplate(operation.requestBody, spec));
    setResponse(null);
  }, [operation, spec]);

  const resolvedUrl = useMemo(() => {
    if (!operation) return '';
    return buildUrl(apiClient.baseUrl, resolvePath(operation.path, pathParams), queryParams);
  }, [operation, apiClient.baseUrl, pathParams, queryParams]);

  const persistHistory = useCallback((entry) => {
    setHistory((prev) => {
      const next = [entry, ...prev].slice(0, HISTORY_LIMIT);
      try {
        localStorage.setItem(STORAGE_KEYS.apiExplorerHistory, JSON.stringify(next));
      } catch {
        /* ignore quota */
      }
      return next;
    });
  }, []);

  const execute = useCallback(async () => {
    if (!operation) return;
    setRunning(true);
    const start = performance.now();
    try {
      const hasBody = !['GET', 'HEAD'].includes(operation.method) && body && body.trim();
      const controller = new AbortController();
      abortRef.current = controller;
      const raw = await apiClient.executeExplorer({
        method: operation.method,
        path: resolvePath(operation.path, pathParams),
        query: queryParams,
        headers,
        body: hasBody ? body : undefined,
        signal: controller.signal
      });
      const respHeaders = {};
      raw.headers.forEach((v, k) => {
        respHeaders[k] = v;
      });
      const contentType = raw.headers.get('content-type') || '';
      const isStream = contentType.includes('text/event-stream') || (raw.headers.get('transfer-encoding') || '').includes('chunked');
      let text = '';
      if (isStream && raw.body) {
        setStreaming(true);
        const reader = raw.body.getReader();
        const decoder = new TextDecoder();
        for (;;) {
          const { value, done } = await reader.read();
          if (done) break;
          text += decoder.decode(value, { stream: true });
          setResponse({ status: raw.status, ok: raw.ok, durationMs: performance.now() - start, size: text.length, headers: respHeaders, body: text, streaming: true });
        }
      } else {
        text = await raw.text();
      }
      const parsed = {
        status: raw.status,
        ok: raw.ok,
        durationMs: performance.now() - start,
        size: text.length,
        headers: respHeaders,
        body: text
      };
      setResponse(parsed);
      persistHistory({
        operationId: operation.id,
        method: operation.method,
        path: operation.path,
        pathParams,
        queryParams,
        headers,
        body,
        status: parsed.status,
        at: new Date().toISOString()
      });
    } catch (e) {
      if (e?.name === 'AbortError') {
        setResponse((prev) => ({ ...(prev || { status: 'ABORTED', headers: {} }), ok: false, streaming: false, aborted: true }));
      } else {
        setResponse({ status: 'ERR', ok: false, durationMs: performance.now() - start, headers: {}, body: String(e.message || e) });
      }
    } finally {
      abortRef.current = null;
      setStreaming(false);
      setRunning(false);
    }
  }, [operation, apiClient, pathParams, queryParams, headers, body, persistHistory]);

  const stop = useCallback(() => {
    abortRef.current?.abort();
  }, []);

  const loadFromHistory = useCallback((h) => {
    skipSeedRef.current = true;
    setOperationId(h.operationId);
    setPathParams(h.pathParams || {});
    setQueryParams(h.queryParams || {});
    setHeaders(h.headers || { Accept: 'application/json' });
    setBody(h.body || '');
  }, []);

  const deleteHistory = useCallback((index) => {
    setHistory((prev) => {
      const next = prev.filter((_, i) => i !== index);
      localStorage.setItem(STORAGE_KEYS.apiExplorerHistory, JSON.stringify(next));
      return next;
    });
  }, []);

  const clearHistory = useCallback(() => {
    setHistory([]);
    localStorage.removeItem(STORAGE_KEYS.apiExplorerHistory);
  }, []);

  return {
    spec,
    specError,
    loadingSpec,
    operations,
    grouped,
    operation,
    operationId,
    setOperationId,
    pathParams,
    setPathParams,
    queryParams,
    setQueryParams,
    headers,
    setHeaders,
    body,
    setBody,
    resolvedUrl,
    response,
    running,
    streaming,
    stop,
    execute,
    history,
    loadFromHistory,
    deleteHistory,
    clearHistory
  };
}
