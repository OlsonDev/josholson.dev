# Architecture

Status: planned. Only the database container exists today; `api/` and `web/` are not built yet.

## Topology

```
Internet -> Caddy (separate server) -> Proxmox guest (Podman compose)
                                         |- web      SvelteKit SSR   host port 8080
                                         |- api      C# API          host port 8081
                                         '- postgres                 internal only
```

- Caddy terminates TLS and `reverse_proxy`s each domain to a mapped host port on the guest:
  - `www.josholson.dev` to `web`
  - `api.josholson.dev` to `api`
- `web` calls `api` over the compose network for server-side rendering. Browser calls to `api.josholson.dev` need CORS for the www origin.
- Firewall the mapped ports to Caddy's IP only.

## Compose files

- `compose.yaml` is production-safe and is the file the server uses. Postgres is bound to `127.0.0.1` on the guest only.
- `compose.dev.yaml` is dev only (durability off for speed). It is never an auto-loaded override; always pass both files explicitly. Never load it on the server.
- Settings and secrets come from an uncommitted `.env` (see `.env.example`).

## Database access

- Postgres publishes `127.0.0.1:${POSTGRES_PORT}` only. In production, reach it from DBeaver through an SSH tunnel to the guest, or use `podman exec -it postgres psql`.
- Data lives in the named volume `postgres-data`. `podman compose down` keeps it; `down -v` deletes it, so never use `-v` in a deploy.
- Back up with a scheduled `pg_dump` to somewhere off the box, plus Proxmox snapshots of the guest. Test a restore.
- Image: `postgres:19rc1-trixie` ([ADR 0003](adr/0003-postgres-19-rc-on-debian-trixie.md)). A major version change needs a dump/restore or `pg_upgrade`.

## Deploy flow

1. Build or pull new `api` and `web` images, tagged by git SHA.
2. Take a `pg_dump`.
3. Run migrations as a one-shot job (`migrate` service under a `tools` profile): `podman compose run --rm migrate`.
4. `podman compose up -d api web`. Only services whose image changed are recreated; `postgres` keeps running.

Migrations are not run on API startup, so the order stays under our control. Write them backward compatible (add before remove), because the old API briefly runs against the new schema.

## Behind the proxy

- SvelteKit (adapter-node) checks the `Origin` header on form POSTs. Set `ORIGIN=https://www.josholson.dev` and the host/protocol header variables, or the contact form and admin login return 403.
- ASP.NET Core: enable forwarded headers and trust Caddy's IP so the app sees the real scheme and client IP.