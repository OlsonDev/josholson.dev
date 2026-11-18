/**
 * Where a response has an instant: a JSON string that becomes a `Temporal.Instant`. A shape is generated for each
 * operation from the OpenAPI document, so a value is converted once, as it arrives, and never half-converted. Build one
 * with `instant`, `arrayOf`, and `objectOf`.
 */
export type Shape =
  | { readonly kind: 'instant' }
  | { readonly kind: 'array'; readonly items: Shape }
  | { readonly kind: 'object'; readonly members: Readonly<Record<string, Shape>> };

/** This value is an instant (`null` stays `null`). */
export const instant: Shape = { kind: 'instant' };

/** This value is an array whose every item has the shape. */
export function arrayOf(items: Shape): Shape {
  return { kind: 'array', items };
}

/** This value is an object, and these members of it have those shapes. The rest are left alone. */
export function objectOf(members: Readonly<Record<string, Shape>>): Shape {
  return { kind: 'object', members };
}

/** Converts the instants in `value`, in place, and returns it. `value` is JSON this call owns, never shared. */
export function revive(value: unknown, shape: Shape): unknown {
  if (value === null || value === undefined) return value;
  if (shape.kind === 'instant') return typeof value === 'string' ? Temporal.Instant.from(value) : value;

  if (shape.kind === 'array') {
    if (!Array.isArray(value)) return value;
    const items: unknown[] = value;
    for (let index = 0; index < items.length; index++) items[index] = revive(items[index], shape.items);
    return items;
  }

  if (typeof value !== 'object') return value;
  for (const [name, member] of Object.entries(shape.members)) {
    if (Reflect.has(value, name)) Reflect.set(value, name, revive(Reflect.get(value, name), member));
  }
  return value;
}
