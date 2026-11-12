CREATE SCHEMA seed;

CREATE TABLE seed.hydration (
    hydration_id  INT          NOT NULL DEFAULT 1
  , layer         TEXT
  , editing       BOOLEAN      NOT NULL DEFAULT FALSE
  , anchor_utc    TIMESTAMPTZ  NOT NULL

  , CONSTRAINT pk_hydration PRIMARY KEY (hydration_id)
  , CONSTRAINT ck_hydration_single_row CHECK (hydration_id = 1)
);
