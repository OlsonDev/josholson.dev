CREATE SCHEMA hash;

CREATE DOMAIN hash.sha256 AS BYTEA
  CONSTRAINT ck_sha256_length
    CHECK (octet_length(VALUE) = 32);
