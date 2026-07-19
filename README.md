# Contoso Policy Assistant

Interview portfolio lab for **Microsoft GCID — Senior Consultant (Full Stack AI Apps)**.  
Vertical slice: RAG + citations, role-based document access, approval-gated agent tickets, Docker/CI, Azure target docs.

Plan: `AI-Tutor/interview/microsoft-gcid-senior-consultant/10-learn-by-doing-cursor-project.md`

## Status

- [x] Day 0 — scaffold
- [x] Day 1 — ask stub + validation
- [x] Day 2 — JWT roles + document ACL
- [x] Day 3 — grounded RAG + citations / refusal
- [x] Day 4 — agent `create_ticket` + HITL + golden evals
- [x] Day 5 — Docker, CI, observability note, consulting docs, demo script

## Quick start (cold start)

```bash
# API
cd src/Api && dotnet run

# Web (other terminal)
cd web && npm install && npm run dev
```

- API: http://localhost:5080/health · Swagger: http://localhost:5080/swagger  
- UI: http://localhost:5173  
- Demo users (password `pass`): `alice` Employee · `bob` Supervisor · `admin` Admin  

No AI key required (default **`Ai:Provider=Lexical`**). Optional Azure OpenAI / OpenAI via user-secrets — see `.env.example`.

### Docker (API)

```bash
docker compose up --build
# API on http://localhost:5080
```

Optional Qdrant (not wired into app code — future swap from in-memory store):

```bash
docker compose --profile qdrant up --build
```

### CI

GitHub Actions (`.github/workflows/ci.yml`): `dotnet test` · `npm run build` · `docker build`.

```powershell
dotnet test
.\scripts\run-evals.ps1
cd web && npm run build
```

## Observability

| Lab today | Azure later |
| --- | --- |
| `RequestLoggingMiddleware` (method/path/status/ms) | Application Insights request telemetry |
| `logs/ai-*.jsonl` (redacted AI/tool traces) | App Insights custom events / Log Analytics |
| `/health` (aiMode, indexChunks, tickets, pending) | Container / App Service health probe |

## Consulting docs

| Doc | Purpose |
| --- | --- |
| [docs/PRD.md](docs/PRD.md) | Problem / success metrics |
| [docs/ADR-001-rag-vs-finetune.md](docs/ADR-001-rag-vs-finetune.md) | Why RAG, not fine-tune |
| [docs/WBS.md](docs/WBS.md) | Discovery → ingest → RAG → agent → harden |
| [docs/RISKS.md](docs/RISKS.md) | Risks + mitigations |
| [docs/AZURE-TARGET-ARCHITECTURE.md](docs/AZURE-TARGET-ARCHITECTURE.md) | Front Door, App Service, Entra, AI Search, AOAI, Key Vault, Monitor |
| [docs/DEMO-SCRIPT.md](docs/DEMO-SCRIPT.md) | 5-minute demo (+ optional Loom) |
| [docs/LEARNING-LOG.md](docs/LEARNING-LOG.md) | Interview talking points |

## Architecture (what we actually built)

```text
React UI → JWT → .NET 8 API
                ├─ PolicyCatalog (allowedRoles)
                ├─ Ingest → InMemoryVectorStore
                ├─ RAG ask (role filter → cite / refuse)
                └─ Agent ask → propose create_ticket → Approve/Reject → TicketStore
```

## Dev users

| User  | Roles                        |
|-------|------------------------------|
| alice | Employee                     |
| bob   | Supervisor, Employee         |
| admin | Admin, Supervisor, Employee  |

## Interview one-liners

- “ACL at **retrieve** time — the model never sees supervisor chunks for an employee.”
- “Write tools need **human gates** — propose, then Approve.”
- “Evals in CI prevent silent groundedness regressions.”
- “Azure shape: Entra + AI Search + Azure OpenAI + App Insights — same contracts.”
