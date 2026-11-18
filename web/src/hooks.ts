import type { Transport } from '@sveltejs/kit/hooks';

// What SvelteKit may send between the server and the browser besides plain JSON (a `load` returns the data of a page, and
// it is serialized into the HTML). An instant from the API is a `Temporal.Instant`, which it cannot send on its own.
export const transport: Transport = {
  Instant: {
    encode: (value) => value instanceof Temporal.Instant && value.toString(),
    decode: (text: string) => Temporal.Instant.from(text),
  },
};
