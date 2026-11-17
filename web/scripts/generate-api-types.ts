import { readFileSync, writeFileSync } from 'node:fs';
import openapiTS, { astToString, COMMENT_HEADER } from 'openapi-typescript';

// Writes the types of what the web sends and receives (the API's payloads and enums) from the API's OpenAPI document:
//   bun scripts/generate-api-types.ts <openapi.json> <schema.ts>        (`mise api:openapi` runs it)
// The route table is left out. It is the API's, not part of what the web sends or receives, it would be two thirds of
// the file, and the generated client reads routes from the document itself. What is committed stays small, so a change
// to the API's surface shows in a handful of lines. The generator prints 4 spaces and no trailing commas, so
// `mise api:openapi` runs `bun fmt` on the result.
const [documentPath, outputPath] = process.argv.slice(2);
if (documentPath === undefined || outputPath === undefined) {
  throw new Error('Usage: bun scripts/generate-api-types.ts <openapi.json> <schema.ts>');
}

const document = JSON.parse(readFileSync(documentPath, 'utf8'));
document.paths = {};

const ast = await openapiTS(document, {
  rootTypes: true,
  rootTypesNoSchemaPrefix: true,
  enum: true,
  conditionalEnums: true,
});
writeFileSync(outputPath, COMMENT_HEADER + astToString(ast));
