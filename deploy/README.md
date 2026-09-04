# Contoso Policy Assistant — production deploy

Disaster-recovery notes for `contoso-policy-assistant` on the Contabo VPS.
Secrets live in `/opt/apps/env/contoso.env` (chmod 600). Never commit that file.

## Project

- Compose project name (`-p`): `contoso`
- Compose file: `deploy/compose.prod.yml`
- Git clone path: `/opt/apps/contoso`
- Branch: `master`
- Dockerfiles: repo-root `Dockerfile` (API), `web/Dockerfile` (UI)
- Dispatch name: `contoso`

## Env file

`--env-file /opt/apps/env/contoso.env`

Variable names (values stay on the box):

`ASPNETCORE_ENVIRONMENT`, `Ai__Provider`, `Ai__Gemini__ApiKey`, `Ai__Gemini__ChatModel`, `Ai__Gemini__EmbeddingModel`, `Ai__OpenAI__ApiKey`, `Ai__OpenAI__ChatModel`, `Ai__OpenAI__EmbeddingModel`, `Ai__DailyRequestCeiling`, `Ai__PerIpLimit`, `Ai__PerIpWindowMinutes`, `Policies__RootPath`, `Cors__Origins`, `Jwt__Key`

Default `Ai__Provider` is `Gemini`. Until `Ai__Gemini__ApiKey` is set, the factory stays on **Lexical**. The key lives only in this env file — never in compose or git. Get it from [Google AI Studio](https://aistudio.google.com/apikey) → Get API key (no credit card). `Jwt__Key` is generated on the box (`openssl rand -base64 48`) and is never the compose/Dockerfile default. `Cors__Origins` is the UI origin (`https://policy.compcodesolutions.com`).

The in-app daily ceiling (`Ai__DailyRequestCeiling`, default 8) and per-IP limit (`Ai__PerIpLimit` / `Ai__PerIpWindowMinutes`) are abuse protection against a crawler burning Gemini free-tier (20 generate RPD on flash-lite). Failed hosted calls refund the in-app counter. Fixed demo questions use a pre-computed answer path and do not consume the ceiling.

After pasting the key, recreate **only** the API (ingest runs on startup; do not bounce other services):

```bash
docker compose -p contoso \
  -f /opt/apps/contoso/deploy/compose.prod.yml \
  --env-file /opt/apps/env/contoso.env \
  up -d --no-deps --force-recreate api
```

## External dependencies

- Docker network: `coolify` (external; Traefik)
- No named volumes (policies baked into the API image)
- Hosts: `policy.compcodesolutions.com` (UI), `api.policy.compcodesolutions.com` (API)
- No published host ports

## Bring up from scratch

```bash
docker compose -p contoso \
  -f /opt/apps/contoso/deploy/compose.prod.yml \
  --env-file /opt/apps/env/contoso.env \
  up -d --build
```

Health: `https://api.policy.compcodesolutions.com/health` (expect 200; `aiMode` is `Gemini` when a key is set, otherwise `Lexical`).
UI: `https://policy.compcodesolutions.com/` (expect 200).

## Shared network — hostnames and recreates

Four apps share the external Docker network `coolify`. Bare service names
(`postgres`, `redis`) are ambiguous: Docker DNS round-robins across every
container that answers that name. Always use the unique hostname
`<project>-<service>-1` (example: `mts-postgres-1`, `rsg-redis-1`).

When recreating an app service, always pass `--no-deps` so Compose does not
restart a database as a side effect:

```bash
docker compose -f deploy/compose.prod.yml --env-file /opt/apps/env/<env>.env \
  -p <project> up -d --no-deps --force-recreate <service>
```

