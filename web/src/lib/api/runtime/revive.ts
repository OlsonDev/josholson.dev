declare const phantom: unique symbol;

type Node =
  | { readonly kind: 'instant' }
  | { readonly kind: 'array'; readonly items: Node }
  | { readonly kind: 'object'; readonly members: Readonly<Record<string, Node>> };

/**
 * Where a response has instants (a JSON string that becomes a `Temporal.Instant`), for a value of type `T`. Generated for
 * each schema that has any, next to its type (`export const PasskeyResponse`), and built from `instant`, `arrayOf`, and
 * `objectOf`, so a value is converted once, as it arrives, and never half-converted. `T` is only for the compiler: it
 * ties a shape to its type, and a call's result to its shape.
 */
export type Shape<T = unknown> = Node & { readonly [phantom]?: T };

/** This value is an instant (`null` stays `null`). */
export const instant: Shape<Temporal.Instant> = { kind: 'instant' };

/** This value is an array whose every item has the shape. */
export function arrayOf<T>(items: Shape<T>): Shape<T[]> {
  return { kind: 'array', items };
}

/** This value is an object, and these members of it have those shapes. The rest are left alone. */
export function objectOf<T extends object>(members: { readonly [K in keyof T]?: Shape<T[K]> }): Shape<T> {
  const nodes: Record<string, Node> = {};
  for (const [name, member] of Object.entries<Shape | undefined>(members)) {
    if (member !== undefined) nodes[name] = member;
  }
  return { kind: 'object', members: nodes };
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
