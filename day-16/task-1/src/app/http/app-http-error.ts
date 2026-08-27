/**
 * Typed application error produced by errorMappingInterceptor (see
 * error-mapping.interceptor.ts) out of a failed HTTP response. Carries a friendly
 * message safe to show a user, plus per-field errors when the real response was a
 * ValidationProblemDetails.
 */
import { HttpErrorResponse } from '@angular/common/http';

export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  readonly instance?: string;
}

export interface ValidationProblemDetails extends ProblemDetails {
  readonly errors: Record<string, string[]>;
}

export class AppHttpError extends Error {
  constructor(
    public readonly friendlyMessage: string,
    public readonly status: number,
    public readonly fieldErrors: Record<string, string[]> | null,
    public readonly originalError: HttpErrorResponse,
  ) {
    super(friendlyMessage);
    this.name = 'AppHttpError';
  }
}

function isProblemDetails(body: unknown): body is ProblemDetails {
  if (typeof body !== 'object' || body === null) {
    return false;
  }
  return 'title' in body || 'status' in body || 'detail' in body || 'type' in body;
}

function isValidationProblemDetails(body: unknown): body is ValidationProblemDetails {
  if (!isProblemDetails(body) || !('errors' in body)) {
    return false;
  }
  const errors = (body as { errors: unknown }).errors;
  return typeof errors === 'object' && errors !== null;
}

// The real QuotesApi (day-3/task-3/QuotesApi) never returns ProblemDetails or
// ValidationProblemDetails -- confirmed by `grep -rn "ProblemDetails|ApiController|
// AddProblemDetails|ValidationProblem"` across its entire source tree (zero matches)
// and by live capture (every observed 4xx there has an empty body). This fallback path
// is therefore the one that matters most for this API, not an edge case: a plain,
// possibly-empty 4xx must still become a sane typed error instead of throwing a raw
// HttpErrorResponse at the caller. A proxy or gateway returning plain text or HTML
// falls down this same path.
function genericMessageForStatus(status: number): string {
  switch (status) {
    case 400:
      return 'That request was invalid.';
    case 401:
      return 'You need to sign in to do that.';
    case 403:
      return "You don't have permission to do that.";
    case 404:
      return 'That could not be found.';
    case 405:
      return 'That request is not supported.';
    default:
      return status >= 500
        ? 'The server had a problem handling that request. Please try again.'
        : 'Something went wrong with that request.';
  }
}

export function mapHttpErrorToAppError(err: HttpErrorResponse): AppHttpError {
  if (err.status === 0) {
    return new AppHttpError(
      'Could not reach the server. Check your connection and try again.',
      0,
      null,
      err,
    );
  }

  const body: unknown = err.error;

  if (isValidationProblemDetails(body)) {
    return new AppHttpError(
      body.title ?? body.detail ?? 'One or more fields are invalid.',
      err.status,
      body.errors,
      err,
    );
  }

  if (isProblemDetails(body)) {
    return new AppHttpError(
      body.detail ?? body.title ?? genericMessageForStatus(err.status),
      err.status,
      null,
      err,
    );
  }

  return new AppHttpError(genericMessageForStatus(err.status), err.status, null, err);
}
