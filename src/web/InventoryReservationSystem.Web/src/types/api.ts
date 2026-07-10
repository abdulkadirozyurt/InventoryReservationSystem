/**
 * Normalized API error shape.
 * Every thrown error from the HTTP client implements this surface so
 * UI code can render status + message uniformly.
 */
export interface ApiErrorPayload {
  status: number;
  code: string;
  message: string;
  details?: unknown;
}

export class ApiError extends Error implements ApiErrorPayload {
  readonly status: number;
  readonly code: string;
  readonly details?: unknown;

  constructor(payload: ApiErrorPayload) {
    super(payload.message);
    this.name = 'ApiError';
    this.status = payload.status;
    this.code = payload.code;
    this.details = payload.details;
  }

  static network(): ApiError {
    return new ApiError({
      status: 0,
      code: 'NetworkError',
      message: 'Could not reach the backend service.',
    });
  }

  static parse(status: number): ApiError {
    return new ApiError({
      status,
      code: 'InvalidResponse',
      message: `Backend returned non-JSON response (HTTP ${status}).`,
    });
  }
}

export function isApiError(err: unknown): err is ApiError {
  return err instanceof ApiError;
}
