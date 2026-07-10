import { ApiError } from '../types/api';

/**
 * Base path for the OrderService REST API. nginx + vite dev proxy
 * both forward `/api` to the backend, so keep this literal.
 */
export const API_BASE = '/api';

/** Default timeout (ms) for one request. */
const DEFAULT_TIMEOUT_MS = 15_000;

interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE' | 'PATCH';
  query?: Record<string, string | number | boolean | undefined>;
  body?: unknown;
  signal?: AbortSignal;
  timeoutMs?: number;
  headers?: Record<string, string>;
}

function buildUrl(path: string, query?: RequestOptions['query']): string {
  const url = new URL(path, window.location.origin);
  if (query) {
    for (const [k, v] of Object.entries(query)) {
      if (v === undefined || v === null) continue;
      url.searchParams.append(k, String(v));
    }
  }
  return url.pathname + (url.search ? url.search : '');
}

/**
 * Low-level fetch with timeout + normalized errors. Every caller gets
 * parsed JSON or an ApiError whose status/code/message are safe to show.
 */
async function fetchParsed<T>(url: string, opts: RequestOptions, init: RequestInit): Promise<T> {
  const timeoutMs = opts.timeoutMs ?? DEFAULT_TIMEOUT_MS;
  const controller = new AbortController();
  const externalSignal = opts.signal;
  if (externalSignal) {
    if (externalSignal.aborted) controller.abort(externalSignal.reason);
    else externalSignal.addEventListener('abort', () => controller.abort(externalSignal.reason));
  }
  const timer = setTimeout(() => controller.abort(new Error('timeout')), timeoutMs);

  try {
    const headers: Record<string, string> = {
      Accept: 'application/json',
      ...(opts.headers ?? {}),
    };
    if (opts.body !== undefined) headers['Content-Type'] = 'application/json';

    const res = await fetch(url, {
      ...init,
      headers,
      body: opts.body !== undefined ? JSON.stringify(opts.body) : undefined,
      signal: controller.signal,
    });

    const contentType = res.headers.get('Content-Type') ?? '';
    const isJson = contentType.includes('application/json');
    const text = await res.text();

    if (!res.ok) {
      let code = 'HttpError';
      let message = res.statusText || `Request failed with HTTP ${res.status}`;
      let details: unknown;
      if (text && isJson) {
        try {
          const payload = JSON.parse(text) as Record<string, unknown>;
          code = (payload['errorCode'] as string) ?? code;
          message = (payload['errorMessage'] as string) ?? (payload['message'] as string) ?? message;
          details = payload;
        } catch {
          /* non-JSON error body */
        }
      } else if (text) {
        message = text;
      }
      throw new ApiError({ status: res.status, code, message, details });
    }
    if (!text) return undefined as T;
    if (!isJson) throw ApiError.parse(res.status);
    return JSON.parse(text) as T;
  } catch (err) {
    if (err instanceof ApiError) throw err;
    if (err instanceof DOMException && err.name === 'AbortError') {
      throw new ApiError({ status: 0, code: 'Timeout', message: 'Request timed out.' });
    }
    if (controller.signal.aborted) {
      throw new ApiError({ status: 0, code: 'Aborted', message: 'Request was aborted.' });
    }
    throw ApiError.network();
  } finally {
    clearTimeout(timer);
  }
}

/** Request against `/api/...` OrderService endpoints. */
export function request<T>(path: string, opts: RequestOptions = {}): Promise<T> {
  const url = buildUrl(API_BASE + path, opts.query);
  return fetchParsed<T>(url, opts, { method: opts.method ?? 'GET' });
}

/** Request against an absolute path (used by `/health` outside of `/api`). */
export function requestAbsolute<T>(path: string, opts: RequestOptions = {}): Promise<T> {
  const url = buildUrl(path, opts.query);
  return fetchParsed<T>(url, opts, { method: opts.method ?? 'GET' });
}
