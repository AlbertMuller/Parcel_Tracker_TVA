import { HttpErrorResponse } from '@angular/common/http';

/** RFC 7807 problem details as returned by the API, plus the extensions it adds. */
export interface Problem {
  status: number;
  title: string;
  detail?: string;
  errors?: Record<string, string[]>;
  existingOrderId?: string;
  existingOrderNumber?: number;
}

/** Turns any HTTP failure into a message a sales rep can act on. */
export function toProblem(error: unknown): Problem {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return {
        status: 0,
        title: 'Could not reach the server',
        detail:
          'Check your connection and try again. If you were submitting an order, it is safe to submit again ' +
          'with the same reference: it will not be duplicated.',
      };
    }
    const body = typeof error.error === 'object' && error.error ? error.error : {};
    return {
      status: error.status,
      title: body.title ?? `Request failed (${error.status})`,
      detail: body.detail,
      errors: body.errors,
      existingOrderId: body.existingOrderId,
      existingOrderNumber: body.existingOrderNumber,
    };
  }
  return { status: -1, title: 'Unexpected error', detail: String(error) };
}
