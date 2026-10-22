CREATE SCHEMA jsonb;

CREATE DOMAIN jsonb.object AS JSONB
  CONSTRAINT ck_object_type
    CHECK (jsonb_typeof(VALUE) = 'object');
