import { API_URL } from '$app/env/private';

export type Tag = {
  tagId: number;
  name: string;
  slug: string;
  description: string | null;
  color: string | null;
};

// Server-side calls only. Browser calls go to api.josholson.dev directly (docs/architecture.md).
export async function getTags(fetch: typeof globalThis.fetch): Promise<Tag[] | null> {
  try {
    const response = await fetch(`${API_URL}/tags`);
    return response.ok ? ((await response.json()) as Tag[]) : null;
  } catch {
    return null;
  }
}
