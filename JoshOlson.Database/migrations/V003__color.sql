CREATE SCHEMA color;

CREATE DOMAIN color.hex AS TEXT
  CONSTRAINT ck_hex_format
    CHECK (VALUE ~ '^#[0-9a-f]{6}$');
