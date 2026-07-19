# Learning log (interview talking points)

## Day 0 — Scaffold
- Created runnable .NET 8 API (`/health`, `/api/info`) + React Vite UI.
- Added 5 sample policies with `allowedRoles` (including supervisor-only safety SOP).
- Cursor rules + PRD + prompt log started.
- Decision: prefer **Azure OpenAI** for the Microsoft interview story; OpenAI works as fallback.

### What I can say tomorrow
“I start engagements with a thin vertical slice and a PRD, not a big-bang AI demo.”

## Day 1 — Ask stub + validation
- Added `POST /api/questions` with ProblemDetails validation (empty / too long).
- React ask form shows field errors and stub echo response.
- xUnit tests for validator + handler echo.
- No LLM yet — contract first, AI later (Day 3).

### What I can say tomorrow
“I ship a validated API contract before wiring the model — same habit I’d use on a customer engagement.”

## Day 2 — JWT roles + document ACL
- Dev login issues JWT with roles (Employee / Supervisor / Admin) — Entra stand-in.
- Protected `GET /api/policies` and `POST /api/questions` (401 without Bearer).
- `PolicyCatalog` parses frontmatter `allowedRoles`; Employees cannot see safety escalate SOP.
- React: login buttons, session token, role in header, ACL-filtered policy list.
- Documented Azure mapping in README (MSAL + app roles + AI Search trimming).

### What I can say tomorrow
“In Azure this becomes Entra app roles plus AI Search security trimming — we filter before the model ever sees the chunk.”

## Day 3 — Grounded RAG + citations
- Ingest: chunk policies → embed → in-memory vectors (keeps `allowedRoles`).
- Ask: embed query → retrieve top-K **role-filtered** → refuse if empty → grounded answer + citations.
- Providers: `Lexical` (offline default), `AzureOpenAI`, `OpenAI` via config/user-secrets.
- UI shows answer, grounded badge, citation list.
- Golden checks: leave days cited; cafeteria refused; employee cannot leak safety SOP.

### What I can say tomorrow
“ACL at retrieve time — the model never sees supervisor chunks for an employee. No context means I don’t know, not a guess.”

## Day 4 — Agent tool + human approval + evals
- `POST /api/agent/ask` — escalate intent → propose `create_ticket` (never auto-write); else RAG.
- Approve / Reject endpoints — Supervisor/Admin only; max 4 steps + timeout.
- Employees cannot propose tickets (`forbiddenTool`).
- AI calls logged to `logs/ai-*.jsonl` with secret redaction.
- `evals/golden.json` (8 cases) + `scripts/run-evals.ps1` / `GoldenEvalTests`.
- Stretch note: thin `CreateTicketTool` is the SK-plugin stand-in (no SK package yet).

### What I can say tomorrow
“Write tools need human gates — propose, then Approve. Evals catch silent RAG regressions in CI.”

## Day 5 — Cloud shape, DevOps, consulting wrap
- Dockerfile + `docker compose` for API; optional Qdrant profile (not wired — in-memory RAG unchanged).
- GitHub Actions: `dotnet test`, `npm run build`, `docker build`.
- Request logging middleware + App Insights mapping in README / Azure arch doc.
- Consulting pack: ADR-001, WBS, RISKS, AZURE-TARGET-ARCHITECTURE, DEMO-SCRIPT.
- Resume bullet added for Contoso Policy Assistant (RAG + approval-gated agent).

### What I can say tomorrow
“Here’s the Azure target architecture and the WBS I’d take a customer through — same vertical slice we just demoed.”
