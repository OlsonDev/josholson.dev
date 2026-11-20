import { readFileSync, writeFileSync } from 'node:fs';

import openapiTS, { astToString, COMMENT_HEADER } from 'openapi-typescript';
import ts from 'typescript';

import { type Constant, inOrder, listName, newUses, type Schemas, shapeOf, successSchemas } from './shapes.ts';

// Writes the types of what the web sends and receives (the API's payloads and enums) from the API's OpenAPI document:
//   bun scripts/generate-api-types.ts <openapi.json> <schema.ts>        (`mise api:openapi` runs it)
// The route table is left out. It is the API's, not part of what the web sends or receives, it would be two thirds of
// the file, and the generated client reads routes from the document itself. What is committed stays small, so a change
// to the API's surface shows in a handful of lines. The generator prints 4 spaces and no trailing commas, so
// `mise api:openapi` runs `bun fmt` on the result.
//
// `openapi-typescript` writes the document's own structure: an `interface components` with every schema in it, referred
// to as `components['schemas']['Foo']`, then an alias for each so they can be imported by name, and an empty member for
// every part of the document we have none of. Here each schema is one `export type Foo = ...` that refers to the others
// by name, and the rest is dropped.
//
// A `date-time` is a `Temporal.Instant`, not a string: the client converts it as the response arrives (see
// src/lib/api/runtime/revive.ts), so the types say what the code holds. A schema with an instant in it also gets a
// constant of the same name, `export const Foo: Shape<Foo>`, that says where (a type and a value can share a name), so the
// shape is by its type and a call only refers to it.
const [documentPath, outputPath] = process.argv.slice(2);
if (documentPath === undefined || outputPath === undefined) {
  throw new Error('Usage: bun scripts/generate-api-types.ts <openapi.json> <schema.ts>');
}

const document = JSON.parse(readFileSync(documentPath, 'utf8'));
// What the operations return is read from the routes before they are left out, for the lists their responses refer to.
const paths = document.paths ?? {};
document.paths = {};
const schemas: Schemas = document.components?.schemas ?? {};

const instant = ts.factory.createTypeReferenceNode(
  ts.factory.createQualifiedName(ts.factory.createIdentifier('Temporal'), 'Instant'),
);

const nodes = await openapiTS(document, {
  rootTypes: true,
  rootTypesNoSchemaPrefix: true,
  enum: true,
  conditionalEnums: true,
  transform: (schema) => (schema.format === 'date-time' ? instant : undefined),
});

// ---- types: one alias for each schema ---------------------------------------------------------------------------------

const exported = [ts.factory.createModifier(ts.SyntaxKind.ExportKeyword)];
const nameOf = (name: ts.PropertyName): string => (ts.isIdentifier(name) || ts.isStringLiteral(name) ? name.text : '');
const enums = new Set(nodes.filter(ts.isEnumDeclaration).map((declaration) => declaration.name.text));

// A reference the tool writes as the text `components['schemas']['Foo']`, as its own aliases are, is `Foo`.
const SCHEMA_REFERENCE = /^components\['schemas'\]\['(.+)'\]$/;
const schemaReferenceName = (node: ts.TypeNode): string | undefined =>
  ts.isTypeReferenceNode(node) && ts.isIdentifier(node.typeName)
    ? SCHEMA_REFERENCE.exec(node.typeName.text)?.[1]
    : undefined;

// `components['schemas']['Foo']` is `Foo`.
function isSchemasOf(node: ts.TypeNode): boolean {
  return (
    ts.isIndexedAccessTypeNode(node) &&
    ts.isTypeReferenceNode(node.objectType) &&
    ts.isIdentifier(node.objectType.typeName) &&
    node.objectType.typeName.text === 'components' &&
    ts.isLiteralTypeNode(node.indexType) &&
    ts.isStringLiteral(node.indexType.literal) &&
    node.indexType.literal.text === 'schemas'
  );
}

function byName<TNode extends ts.Node>(root: TNode): TNode {
  const result = ts.transform(root, [
    (context) => {
      const visit = (node: ts.Node): ts.Node => {
        if (
          ts.isIndexedAccessTypeNode(node) &&
          isSchemasOf(node.objectType) &&
          ts.isLiteralTypeNode(node.indexType) &&
          ts.isStringLiteral(node.indexType.literal)
        ) {
          return ts.factory.createTypeReferenceNode(node.indexType.literal.text);
        }
        if (ts.isTypeNode(node) && schemaReferenceName(node) !== undefined) {
          return ts.factory.createTypeReferenceNode(schemaReferenceName(node) ?? '');
        }
        return ts.visitEachChild(node, visit, context);
      };
      return (node) => ts.visitNode(node, visit) ?? node;
    },
  ]);
  const [only] = result.transformed;
  if (only === undefined) throw new Error('the transform produced nothing');
  return only;
}

const statements: ts.Node[] = [];
const names: string[] = [];
for (const node of nodes) {
  if (ts.isInterfaceDeclaration(node) && node.name.text === 'components') {
    const schemasMember = node.members.find((member) => member.name !== undefined && nameOf(member.name) === 'schemas');
    if (schemasMember === undefined || !ts.isPropertySignature(schemasMember) || schemasMember.type === undefined) {
      throw new Error('components has no schemas');
    }
    if (!ts.isTypeLiteralNode(schemasMember.type)) throw new Error('components.schemas is not an object type');
    for (const member of schemasMember.type.members) {
      if (!ts.isPropertySignature(member) || member.type === undefined) continue;
      const name = nameOf(member.name);
      // An enum is declared as an enum below, and is its own type.
      if (ts.isTypeReferenceNode(member.type) && ts.isIdentifier(member.type.typeName) && enums.has(name)) continue;
      const alias = ts.factory.createTypeAliasDeclaration(exported, name, undefined, byName(member.type));
      ts.setSyntheticLeadingComments(alias, ts.getSyntheticLeadingComments(member));
      statements.push(alias);
      names.push(name);
    }
  } else if (ts.isTypeAliasDeclaration(node) && schemaReferenceName(node.type) !== undefined) {
    // `export type Foo = components['schemas']['Foo']`, which the aliases above replace.
  } else if (ts.isTypeAliasDeclaration(node) && ['paths', 'webhooks', '$defs', 'operations'].includes(node.name.text)) {
    // The parts of the document we have none of.
  } else {
    statements.push(node);
  }
}

// ---- shapes: a constant for each schema that has an instant in it, and each list of one that is used --------------------

const constants: Constant[] = [];
const lists = new Set<string>();
for (const name of names) {
  const body = schemas[name];
  if (body === undefined) continue;
  const uses = newUses();
  const code = shapeOf(body, schemas, name, uses, lists, name, [name]);
  if (code === undefined) continue;
  constants.push({ name, type: name, code, uses });
}
// What a response is an array of is a list too, even when no schema refers to it.
for (const { where, schema } of successSchemas(paths)) shapeOf(schema, schemas, where, newUses(), lists);
for (const name of [...lists].toSorted()) {
  const uses = newUses();
  uses.runtime.add('arrayOf');
  uses.constants.add(name);
  constants.push({ name: listName(name), type: `${name}[]`, code: `arrayOf(${name})`, uses });
}

const ordered = inOrder(constants);
const runtimeNames = [...new Set(ordered.flatMap((constant) => [...constant.uses.runtime]))].toSorted();
const header =
  ordered.length === 0
    ? ''
    : `import { ${[...runtimeNames, 'type Shape'].join(', ')} } from './runtime/revive.ts';\n\n`;
const shapes = ordered
  .map(
    (constant) =>
      `export const ${constant.name}: Shape<${constant.type}> = ${/^[A-Za-z_$][A-Za-z0-9_$]*$/.test(constant.code) ? '' : '/*#__PURE__*/ '}${constant.code};\n`,
  )
  .join('\n');

const printed = astToString(statements);
writeFileSync(outputPath, `${COMMENT_HEADER}${header}${printed}${shapes === '' ? '' : `\n${shapes}`}`);
