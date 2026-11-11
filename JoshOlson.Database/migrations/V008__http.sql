CREATE SCHEMA http;

CREATE TABLE http.user_agent (
    user_agent_id  INT           NOT NULL GENERATED ALWAYS AS IDENTITY
  , value          text.trimmed  NOT NULL

  , CONSTRAINT pk_user_agent PRIMARY KEY (user_agent_id)
  , CONSTRAINT uq_user_agent_value UNIQUE (value)
  , CONSTRAINT ck_user_agent_value_length CHECK (length(value) <= 512)
);

COMMENT ON TABLE http.user_agent IS '@seed ignore';
