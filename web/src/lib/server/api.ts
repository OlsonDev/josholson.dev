import { API_URL } from '$app/env/private';
import type { TagResponse } from '#lib/api/schema.ts';

// Server-side calls only. Browser calls go to api.josholson.dev directly (docs/architecture.md).
export async function getTags(fetch: typeof globalThis.fetch): Promise<TagResponse[] | null> {
  try {
    const response = await fetch(`${API_URL}/tags`);
    return response.ok ? ((await response.json()) as TagResponse[]) : null;
  } catch {
    return null;
  }
}
