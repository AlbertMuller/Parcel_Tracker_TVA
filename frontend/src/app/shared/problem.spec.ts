import { HttpErrorResponse } from '@angular/common/http';
import { toProblem } from './problem';

describe('toProblem', () => {
  it('reassures the user that retrying is safe when the server is unreachable', () => {
    const problem = toProblem(new HttpErrorResponse({ status: 0 }));
    expect(problem.title).toBe('Could not reach the server');
    expect(problem.detail).toContain('same reference');
  });

  it('keeps the API problem details, including the existing order for a reference conflict', () => {
    const problem = toProblem(
      new HttpErrorResponse({
        status: 409,
        error: { title: 'External reference already used', detail: 'Already used', existingOrderId: 'abc', existingOrderNumber: 7 },
      }),
    );
    expect(problem).toEqual({
      status: 409,
      title: 'External reference already used',
      detail: 'Already used',
      errors: undefined,
      existingOrderId: 'abc',
      existingOrderNumber: 7,
    });
  });

  it('exposes field-level validation errors', () => {
    const problem = toProblem(
      new HttpErrorResponse({ status: 400, error: { title: 'Invalid', errors: { 'lineItems[0].quantity': ['Bad'] } } }),
    );
    expect(problem.errors).toEqual({ 'lineItems[0].quantity': ['Bad'] });
  });
});
