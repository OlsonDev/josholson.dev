// Where a response has instants, worked out from the OpenAPI document, for both generators: `generate-api-types.ts`
// writes a shape constant next to each schema's type, and `generate-api-client.ts` refers to them by name. A shape is
// code (see src/lib/api/runtime/revive.ts): `instant`, `arrayOf(...)`, `objectOf({ ... })`, or the name of a constant.
//
// The constants are, for each schema that has an instant in it (`Instant` itself among them), `Foo`, and, for each one
// something has an array of, `FooList`. They are built once, where the schema is, and what refers to one refers to it by name.

export type Schema = {
  $ref?: string;
  type?: string | string[];
  format?: string;
  items?: Schema;
  oneOf?: Schema[];
  anyOf?: Schema[];
  allOf?: Schema[];
  properties?: Record<string, Schema>;
  required?: string[];
  additionalProperties?: Schema | boolean;
  enum?: unknown[];
};

export type Schemas = Record<string, Schema>;

/** What a piece of generated code uses: runtime names (`instant`, ...), and constants (`Foo`, `FooList`) by name. */
export type Uses = { runtime: Set<string>; constants: Set<string> };

export const newUses = (): Uses => ({ runtime: new Set(), constants: new Set() });

export const key = (name: string): string => (/^[A-Za-z_$][A-Za-z0-9_$]*$/.test(name) ? name : JSON.stringify(name));

export function refName(ref: string): string {
  const name = ref.split('/').pop();
  if (name === undefined || name === '') throw new Error(`${ref} is not a reference to a schema`);
  return name;
}

/** The name of the constant for an array of a schema's values. */
export const listName = (schema: string): string => `${schema}List`;

const isInstant = (schema: Schema): boolean => {
  const types = Array.isArray(schema.type) ? schema.type : [schema.type];
  return types.includes('string') && schema.format === 'date-time';
};

/**
 * The code for the shape of a value of this schema, or undefined when it holds no instant. A schema that has instants in
 * it is referred to by its constant, and an array of one by its list's. A `date-time` that is not a schema of its own is
 * `instant`, the runtime's. `ofType` is the
 * TypeScript type of the value, for an outermost `objectOf`, so the compiler ties the shape to it. Every list the code
 * refers to is added to `lists`, for the types generator to write.
 */
export function shapeOf(
  schema: Schema,
  schemas: Schemas,
  where: string,
  uses: Uses,
  lists: Set<string>,
  ofType?: string,
  path: string[] = [],
): string | undefined {
  if (schema.$ref !== undefined) {
    const name = refName(schema.$ref);
    const body = schemas[name];
    if (body === undefined) throw new Error(`${where}: ${schema.$ref} is not in the document`);
    if (path.includes(name)) {
      throw new Error(`${where}: ${name} refers to itself, which the generator cannot convert yet`);
    }
    if (shapeOf(body, schemas, where, newUses(), new Set(), undefined, [...path, name]) === undefined) return undefined;
    uses.constants.add(name);
    return name;
  }
  const members = schema.oneOf ?? schema.anyOf ?? (schema.allOf?.length === 1 ? schema.allOf : undefined);
  if (members !== undefined) {
    return members
      .map((member) => shapeOf(member, schemas, where, uses, lists, ofType, path))
      .find((shape) => shape !== undefined);
  }
  if (isInstant(schema)) return use(uses, 'instant');
  const types = Array.isArray(schema.type) ? schema.type : [schema.type];

  // A dictionary's values are not named members, so a shape cannot say where their instants are.
  const values = schema.additionalProperties;
  if (
    typeof values === 'object' &&
    shapeOf(values, schemas, where, newUses(), new Set(), undefined, path) !== undefined
  ) {
    throw new Error(`${where}: a dictionary of values with instants in them is not supported yet`);
  }

  if (types.includes('array') && schema.items !== undefined) {
    const items = shapeOf(schema.items, schemas, where, uses, lists, undefined, path);
    if (items === undefined) return undefined;
    // An array of a schema is that schema's list, so every array of it shares one.
    if (schema.items.$ref !== undefined) {
      const name = refName(schema.items.$ref);
      if (listName(name) in schemas)
        throw new Error(`${where}: ${listName(name)} is a schema, and the name of ${name}'s list`);
      lists.add(name);
      uses.constants.add(listName(name));
      return listName(name);
    }
    return `${use(uses, 'arrayOf')}(${items})`;
  }
  if (types.includes('object') && schema.properties !== undefined) {
    const parts: string[] = [];
    for (const [name, member] of Object.entries(schema.properties)) {
      const shape = shapeOf(member, schemas, where, uses, lists, undefined, path);
      if (shape !== undefined) parts.push(`${key(name)}: ${shape}`);
    }
    return parts.length === 0
      ? undefined
      : `${use(uses, 'objectOf')}${ofType === undefined ? '' : `<${ofType}>`}({ ${parts.join(', ')} })`;
  }
  return undefined;
}

function use(uses: Uses, name: string): string {
  uses.runtime.add(name);
  return name;
}

/** A constant to write: its name, the type its shape is of, the code that builds it, and what that code uses. */
export type Constant = { name: string; type: string; code: string; uses: Uses };

/**
 * Puts each constant after the ones it refers to, and otherwise in name order: of those that are ready, the smallest
 * name goes next. The order depends only on the names and what refers to what, so a new schema is where its name puts it
 * unless something it uses has to come first.
 */
export function inOrder(constants: Constant[]): Constant[] {
  const byName = new Map(constants.map((constant) => [constant.name, constant]));
  const waiting = new Map<string, Set<string>>();
  for (const constant of constants) {
    for (const dependency of constant.uses.constants) {
      if (!byName.has(dependency)) throw new Error(`${constant.name} refers to ${dependency}, which has no constant`);
    }
    waiting.set(constant.name, new Set(constant.uses.constants));
  }
  const ordered: Constant[] = [];
  while (waiting.size > 0) {
    const [next] = [...waiting]
      .filter(([, dependencies]) => dependencies.size === 0)
      .map(([name]) => name)
      .toSorted();
    const constant = next === undefined ? undefined : byName.get(next);
    if (next === undefined || constant === undefined) {
      throw new Error(`these refer to one another: ${[...waiting.keys()].toSorted().join(', ')}`);
    }
    ordered.push(constant);
    waiting.delete(next);
    for (const dependencies of waiting.values()) dependencies.delete(next);
  }
  return ordered;
}

type Responses = Record<string, { content?: Record<string, { schema?: Schema }> }>;
type Paths = Record<string, Record<string, { responses?: Responses }>>;

/** The JSON response schema of every operation's success, with where to say it is from. */
export function successSchemas(paths: Paths): { where: string; schema: Schema }[] {
  const found: { where: string; schema: Schema }[] = [];
  for (const [path, item] of Object.entries(paths)) {
    for (const [method, operation] of Object.entries(item)) {
      for (const [status, response] of Object.entries(operation.responses ?? {})) {
        const schema = response.content?.['application/json']?.schema;
        if (status.startsWith('2') && schema !== undefined)
          found.push({ where: `${method.toUpperCase()} ${path}`, schema });
      }
    }
  }
  return found;
}
