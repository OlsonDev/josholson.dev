CREATE SCHEMA color;

CREATE DOMAIN color.hex AS TEXT
  CONSTRAINT ck_hex_format
    CHECK (VALUE ~ '^#[0-9a-f]{6}$');

CREATE DOMAIN color.tailwind AS TEXT
  CONSTRAINT ck_tailwind_format
    CHECK (
      VALUE ~ '^(black|white|(red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose|slate|gray|zinc|neutral|stone|taupe|mauve|mist|olive)-(50|100|200|300|400|500|600|700|800|900|950))$'
    );
