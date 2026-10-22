CREATE SCHEMA temporal;

CREATE DOMAIN temporal.year_optional_month AS TEXT
  CONSTRAINT ck_year_optional_month_format
    CHECK (VALUE ~ '^\d{4}(-(0[1-9]|1[0-2]))?$');