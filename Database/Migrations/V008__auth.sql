CREATE SCHEMA auth;

CREATE TABLE auth.user (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , display_name TEXT NOT NULL
  , is_active BOOLEAN NOT NULL DEFAULT TRUE
  , created_utc TIMESTAMPTZ NOT NULL DEFAULT now()
  , CONSTRAINT pk_user PRIMARY KEY (id)
  , CONSTRAINT ck_user_display_name_not_empty CHECK (display_name != '')
);

CREATE TABLE auth.user_email (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , user_id INT NOT NULL
  , email TEXT NOT NULL
  , is_primary BOOLEAN NOT NULL DEFAULT FALSE
  , verified_utc TIMESTAMPTZ NULL
  , created_utc TIMESTAMPTZ NOT NULL DEFAULT now()
  , CONSTRAINT pk_user_email PRIMARY KEY (id)
  , CONSTRAINT fk_user_email_user FOREIGN KEY (user_id)
      REFERENCES auth.user (id) ON DELETE CASCADE
  , CONSTRAINT ck_user_email_email_format CHECK (email = btrim(email) AND position('@' IN email) > 1)
);

CREATE UNIQUE INDEX ux_user_email_email_lower ON auth.user_email (lower(email));

CREATE UNIQUE INDEX ux_user_email_one_primary_per_user ON auth.user_email (user_id) WHERE is_primary;

CREATE INDEX ix_user_email_user_id ON auth.user_email (user_id);

CREATE TABLE auth.identity (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , user_id INT NOT NULL
  , provider TEXT NOT NULL
  , subject TEXT NOT NULL
  , password_hash TEXT NULL
  , data JSONB NOT NULL DEFAULT '{}'
  , created_utc TIMESTAMPTZ NOT NULL DEFAULT now()
  , last_used_utc TIMESTAMPTZ NULL
  , CONSTRAINT pk_identity PRIMARY KEY (id)
  , CONSTRAINT uq_identity_provider_subject UNIQUE (provider, subject)
  , CONSTRAINT fk_identity_user FOREIGN KEY (user_id)
      REFERENCES auth.user (id) ON DELETE CASCADE
  , CONSTRAINT ck_identity_provider_format CHECK (provider != '' AND provider = lower(provider))
  , CONSTRAINT ck_identity_subject_not_empty CHECK (subject != '')
  , CONSTRAINT ck_identity_password_requires_hash CHECK (provider != 'password' OR password_hash IS NOT NULL)
);

CREATE INDEX ix_identity_user_id ON auth.identity (user_id);

CREATE TABLE auth.session (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , user_id INT NOT NULL
  , token_hash BYTEA NOT NULL
  , created_utc TIMESTAMPTZ NOT NULL DEFAULT now()
  , expires_utc TIMESTAMPTZ NOT NULL
  , last_seen_utc TIMESTAMPTZ NOT NULL DEFAULT now()
  , ip INET NULL
  , user_agent TEXT NULL
  , CONSTRAINT pk_session PRIMARY KEY (id)
  , CONSTRAINT uq_session_token_hash UNIQUE (token_hash)
  , CONSTRAINT fk_session_user FOREIGN KEY (user_id)
      REFERENCES auth.user (id) ON DELETE CASCADE
  , CONSTRAINT ck_session_token_hash_length CHECK (octet_length(token_hash) = 32)
  , CONSTRAINT ck_session_expires_after_created CHECK (expires_utc > created_utc)
);

CREATE INDEX ix_session_user_id ON auth.session (user_id);

CREATE INDEX ix_session_expires_utc ON auth.session (expires_utc);