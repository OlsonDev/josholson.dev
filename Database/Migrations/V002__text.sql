CREATE SCHEMA text;

CREATE DOMAIN text.trimmed AS TEXT
  CONSTRAINT ck_trimmed_format
    CHECK (VALUE ~ '^\S(.*\S)?$');

CREATE DOMAIN text.kebab_case AS TEXT
  CONSTRAINT ck_kebab_case_format
    CHECK (VALUE ~ '^[a-z0-9]+(-[a-z0-9]+)*$');

CREATE DOMAIN text.url AS TEXT
  CONSTRAINT ck_url_format
    CHECK (VALUE ~* '^https?://[^\s/?#]+([/?#]\S*)?$' AND length(VALUE) <= 2048);

CREATE DOMAIN text.email AS TEXT
  CONSTRAINT ck_email_format
    CHECK (VALUE ~ '^[^@\s]+@[^@\s]+\.[^@\s]+$' AND length(VALUE) <= 254);
