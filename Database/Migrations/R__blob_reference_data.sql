INSERT INTO blob.mime_type (name, is_image)
VALUES
    ('application/pdf', FALSE)
  , ('image/avif', TRUE)
  , ('image/gif', TRUE)
  , ('image/jpeg', TRUE)
  , ('image/png', TRUE)
  , ('image/svg+xml', TRUE)
  , ('image/webp', TRUE)
ON CONFLICT (name) DO UPDATE
SET is_image = EXCLUDED.is_image;

INSERT INTO blob.file_extension (name)
VALUES
    ('avif')
  , ('gif')
  , ('jpeg')
  , ('jpg')
  , ('pdf')
  , ('png')
  , ('svg')
  , ('webp')
ON CONFLICT (name) DO NOTHING;

UPDATE blob.mime_type__file_extension
SET is_preferred = FALSE
WHERE is_preferred;

INSERT INTO blob.mime_type__file_extension (mime_type_id, file_extension_id, is_preferred)
SELECT
    m.mime_type_id
  , e.file_extension_id
  , v.is_preferred
FROM (
  VALUES
      ('application/pdf', 'pdf', TRUE)
    , ('image/avif', 'avif', TRUE)
    , ('image/gif', 'gif', TRUE)
    , ('image/jpeg', 'jpg', TRUE)
    , ('image/jpeg', 'jpeg', FALSE)
    , ('image/png', 'png', TRUE)
    , ('image/svg+xml', 'svg', TRUE)
    , ('image/webp', 'webp', TRUE)
) AS v (mime_type, file_extension, is_preferred)
JOIN blob.mime_type m ON m.name = v.mime_type
JOIN blob.file_extension e ON e.name = v.file_extension
ON CONFLICT (mime_type_id, file_extension_id) DO UPDATE
SET is_preferred = EXCLUDED.is_preferred;