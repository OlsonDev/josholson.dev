import type { ProblemDetails, ValidationProblemDetails } from '../schema.ts';

/** What the API sends with an error: the problem details, with an `errors` map when a field was refused. */
export type Problem = ProblemDetails | ValidationProblemDetails;

/**
 * A call the API answered with an error status, or never answered at all (`status` is 0). Thrown by every generated
 * function. An aborted call is not an `ApiError`: it rejects with the `AbortSignal`'s reason, as `fetch` does.
 */
export class ApiError extends Error {
  /** The HTTP status, or 0 when there was no response because the network failed. */
  readonly status: number;

  /** The operation that failed, such as `tag.get`. */
  readonly operation: string;

  /** The problem details the API sent, when it sent any. */
  readonly problem: Problem | undefined;

  constructor(operation: string, status: number, problem: Problem | undefined, options?: ErrorOptions) {
    super(describe(operation, status, problem), options);
    this.name = 'ApiError';
    this.operation = operation;
    this.status = status;
    this.problem = problem;
  }

  /**
   * Each field the API refused and what is wrong with it, keyed by JSON name (`slug`, `children[0].label`). A field the
   * body had the wrong type for arrives from the API as `$.slug`, and is `slug` here. Empty unless the API sent errors.
   */
  get fieldErrors(): Readonly<Record<string, readonly string[]>> {
    return split(this.problem).fields;
  }

  /**
   * What is wrong with the request as a whole, not one field: a rule across fields (the API puts those under `""`), and a
   * body that could not be read as JSON (under `$`). For the summary at the top of a form.
   */
  get formErrors(): readonly string[] {
    return split(this.problem).form;
  }

  /**
   * Every message, for a toast or a list: what is wrong with the request as a whole, then each field's. When the API
   * sent no validation errors, the one message it sent (`detail`, or else `title`), or none.
   */
  get messages(): readonly string[] {
    const { fields, form } = split(this.problem);
    const all = [...form, ...Object.values(fields).flat()];
    if (all.length > 0) return all;
    const single = this.problem?.detail ?? this.problem?.title;
    return single === undefined || single === null ? [] : [single];
  }

  /** The ID of the request, for finding it in the API's logs. */
  get traceId(): string | undefined {
    return this.problem?.traceId;
  }

  /** Whether the API answered with this status. */
  is(status: number): boolean {
    return this.status === status;
  }
}

// The API's `errors` map by who each error is about. `""` and `$` are the whole request; `$.slug` is `slug` with a prefix
// the JSON reader adds, and an error under both spellings is one list.
function split(problem: Problem | undefined): { fields: Record<string, string[]>; form: string[] } {
  const fields: Record<string, string[]> = {};
  const form: string[] = [];
  const errors = problem !== undefined && 'errors' in problem ? (problem.errors ?? {}) : {};
  for (const [key, messages] of Object.entries(errors)) {
    if (key === '' || key === '$') {
      form.push(...messages);
      continue;
    }
    const field = key.startsWith('$.') ? key.slice(2) : key;
    fields[field] = [...(fields[field] ?? []), ...messages];
  }
  return { fields, form };
}

function describe(operation: string, status: number, problem: Problem | undefined): string {
  const outcome = status === 0 ? 'got no response' : `got ${status.toString()}`;
  const title = problem?.title;
  return title === undefined || title === null ? `${operation} ${outcome}` : `${operation} ${outcome}: ${title}`;
}
