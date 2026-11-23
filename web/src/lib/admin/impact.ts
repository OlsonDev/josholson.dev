import { ApiError } from '#lib/api/index.ts';
import type { DeletionImpact } from '#lib/api/schema.ts';

/** Whether a value is what the API sends to say what deleting something affects. */
export function isDeletionImpact(value: unknown): value is DeletionImpact {
  return (
    typeof value === 'object' &&
    value !== null &&
    'blockers' in value &&
    Array.isArray(value.blockers) &&
    'warnings' in value &&
    Array.isArray(value.warnings)
  );
}

/** What blocked a delete, when the API refused it with a 409 and said why. Null for any other failure. */
export function impactFromError(error: unknown): DeletionImpact | null {
  if (!(error instanceof ApiError) || !error.is(409)) return null;
  const body: unknown = error.problem;
  return isDeletionImpact(body) ? body : null;
}

/** Whether anything makes the delete impossible. */
export function isBlocked(impact: DeletionImpact): boolean {
  return impact.blockers.length > 0;
}
