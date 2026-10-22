# ADR 0008: Content-addressed blobs

- Status: Accepted
- Date: 2026-10-21

## Context

Icons, avatars, and cover images should be high-resolution files stored on the server, not slugs into an icon font.

## Decision

A dedicated `blob` schema. `blob.blob` stores metadata (SHA-256, mime type, size, dimensions, original file name). The file itself sits on a server volume, named by its hash, so its path is derived and not stored. `mime_type`, `file_extension`, and a many-to-many `mime_type_file_extension` (with one preferred extension per type) describe types.

## Alternatives considered

- `bytea` in Postgres: simpler, with a single backup, but bulkier dumps and awkward binary in seed YAML.
- `media`, `asset`, or `file` as the schema name: `blob` makes no promise about the storage backend.
- A `storage_kind` column for a future S3 move: not needed now; a migration can assume the original location.

## Consequences

- Files are immutable and deduplicated, so every database cloned from a template can share one directory.
- Foreign keys into `blob.blob` are `RESTRICT`. Deleting a skill must not delete its file; an unreferenced-blob sweep is still to be written.
- Uploads: sniff the content type server-side, strip EXIF/GPS from photos, sanitize SVG, serve with `nosniff`, and never build a path from the original file name.
- Alt text belongs to the use of a blob, not the blob.