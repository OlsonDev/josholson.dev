import { describe, expect, test } from 'bun:test';
import { inOrder, newUses, shapeOf, successSchemas, type Constant, type Schemas } from './shapes.ts';

const instantSchema = { type: 'string', format: 'date-time' };
const schemas: Schemas = {
  Instant: instantSchema,
  Foo: {
    type: 'object',
    properties: { id: { type: 'integer' }, createdUtc: { $ref: '#/components/schemas/Instant' } },
  },
  Bar: { type: 'object', properties: { name: { type: 'string' } } },
  Baz: { type: 'object', properties: { foos: { type: 'array', items: { $ref: '#/components/schemas/Foo' } } } },
};

function shape(schema: Schemas[string], among: Schemas = schemas): { code: string | undefined; lists: string[] } {
  const lists = new Set<string>();
  const code = shapeOf(schema, among, 'test', newUses(), lists);
  return { code, lists: [...lists] };
}

describe('shapeOf', () => {
  test('is undefined for what has no instant, `Instant` for the schema, and `instant` for a bare one', () => {
    expect(shape({ $ref: '#/components/schemas/Bar' }).code).toBeUndefined();
    expect(shape({ $ref: '#/components/schemas/Instant' }).code).toBe('Instant');
    expect(shape(instantSchema).code).toBe('instant');
    expect(shape({ type: 'array', items: { type: 'string' } }).code).toBeUndefined();
  });

  test('is a schema that has an instant by its name', () => {
    expect(shape({ $ref: '#/components/schemas/Foo' }).code).toBe('Foo');
  });

  test('is an array of a schema by its list, and says the list is needed', () => {
    const result = shape({ type: 'array', items: { $ref: '#/components/schemas/Foo' } });

    expect(result.code).toBe('FooList');
    expect(result.lists).toEqual(['Foo']);
  });

  test('a schema holding an array of another refers to its list', () => {
    const result = shape(schemas['Baz'] ?? {});

    expect(result.code).toBe('objectOf({ foos: FooList })');
    expect(result.lists).toEqual(['Foo']);
  });

  test('an array of instants is the list of Instant, as it is a schema like any other', () => {
    const result = shape({ type: 'array', items: { $ref: '#/components/schemas/Instant' } });

    expect(result.code).toBe('InstantList');
    expect(result.lists).toEqual(['Instant']);
  });

  test('a dictionary of values with instants fails, and one without does not', () => {
    const withInstants = { type: 'object', additionalProperties: { $ref: '#/components/schemas/Foo' } };

    expect(() => shape(withInstants)).toThrow('a dictionary of values with instants');
    expect(shape({ type: 'object', additionalProperties: { type: 'string' } }).code).toBeUndefined();
  });

  test('a schema that refers to itself fails', () => {
    const loop: Schemas = {
      Instant: instantSchema,
      Node: { type: 'object', properties: { at: instantSchema, next: { $ref: '#/components/schemas/Node' } } },
    };

    expect(() => shape({ $ref: '#/components/schemas/Node' }, loop)).toThrow('refers to itself');
  });

  test('a list whose name is a schema of its own fails', () => {
    const clash: Schemas = { ...schemas, FooList: { type: 'object', properties: {} } };

    expect(() => shape({ type: 'array', items: { $ref: '#/components/schemas/Foo' } }, clash)).toThrow(
      'FooList is a schema',
    );
  });
});

function constant(name: string, ...uses: string[]): Constant {
  const used = newUses();
  for (const other of uses) used.constants.add(other);
  return { name, type: name, code: name, uses: used };
}

describe('inOrder', () => {
  test('is by name when nothing refers to anything', () => {
    expect(inOrder([constant('C'), constant('A'), constant('B')]).map((c) => c.name)).toEqual(['A', 'B', 'C']);
  });

  test('puts what is used first, and is by name otherwise', () => {
    const names = inOrder([constant('A', 'D'), constant('B'), constant('C', 'D'), constant('D')]).map((c) => c.name);

    expect(names).toEqual(['B', 'D', 'A', 'C']);
  });

  test('does not depend on the order it is given', () => {
    const given = [constant('FooList', 'Foo'), constant('Foo'), constant('Bar', 'FooList'), constant('Baz')];

    expect(inOrder(given).map((c) => c.name)).toEqual(inOrder(given.toReversed()).map((c) => c.name));
    expect(inOrder(given).map((c) => c.name)).toEqual(['Baz', 'Foo', 'FooList', 'Bar']);
  });

  test('fails on a cycle, and on a constant that does not exist', () => {
    expect(() => inOrder([constant('A', 'B'), constant('B', 'A')])).toThrow('refer to one another: A, B');
    expect(() => inOrder([constant('A', 'Z')])).toThrow('has no constant');
  });
});

test('successSchemas finds the JSON success of every operation', () => {
  const found = successSchemas({
    '/foos': {
      get: { responses: { '200': { content: { 'application/json': { schema: { type: 'array' } } } }, '404': {} } },
      delete: { responses: { '204': {} } },
    },
  });

  expect(found).toEqual([{ where: 'GET /foos', schema: { type: 'array' } }]);
});
