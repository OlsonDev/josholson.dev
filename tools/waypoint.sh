#!/bin/sh
# Runs waypoint against the compose postgres service. The connection URL is built
# from the POSTGRES_* variables, percent-encoded so passwords may hold any character.
encode() {
  rest=$1
  while [ -n "$rest" ]; do
    rest_tail=${rest#?}
    char=${rest%"$rest_tail"}
    rest=$rest_tail
    case $char in
      [A-Za-z0-9._~-]) printf '%s' "$char" ;;
      *) printf '%%%02X' "'$char" ;;
    esac
  done
}

url="postgres://$(encode "$POSTGRES_USER"):$(encode "$POSTGRES_PASSWORD")@postgres:5432/$(encode "$POSTGRES_DB")"
exec /usr/local/libexec/waypoint --url "$url" --locations /migrations "$@"
