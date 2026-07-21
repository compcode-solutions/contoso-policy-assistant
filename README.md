# Contoso Policy Assistant

A full-stack sample app that helps employees ask questions about company policies and get **answers backed by real documents** — with role-based access control and human approval before any write action.

**Stack:** .NET 8 Web API · React + TypeScript · RAG (Retrieval-Augmented Generation) · JWT auth · Docker · Playwright e2e tests

---

## What does this app do?

Imagine an internal HR assistant for Contoso Corp:

1. **Sign in** with your role (Employee, Supervisor, or Admin).
2. **See only the policies** you are allowed to read — e.g. Employees cannot see supervisor-only safety procedures.
3. **Ask a question** such as *“How many leave days do I get?”*
4. The app **searches policy documents**, finds relevant sections, and returns an answer **with citations** to the source.
5. If the question is outside the policies (e.g. *“What’s the cafeteria menu?”*), the app **refuses** instead of guessing.
6. For urgent escalations, a **Supervisor can propose a ticket** — but nothing is written until another human clicks **Approve**.

In short: grounded Q&A over documents, security by role, and human-in-the-loop for actions that change data.

---

## Key features

| Feature | What it means |
| --- | --- |
| **Grounded RAG** | Answers come from retrieved policy text, not the model’s memory |
| **Citations** | Each answer links to the policy chunks used |
| **Refusal** | No matching context → “I don’t know”, not a hallucination |
| **Role-based ACL** | Documents filtered **before** search — the model never sees forbidden content |
| **Agent + HITL** | `create_ticket` is proposed, then Approve/Reject — no auto-write |
| **Swappable AI** | Works offline with `Lexical` mode; optional Azure OpenAI / OpenAI via config |
| **Tests & evals** | Unit tests + golden eval cases for cite/refuse/ACL/ticket rules |
| **Container-ready** | Dockerfile + `docker compose` for the API |

---

## How it works (request flow)

```text
User (React UI)
    │
    ▼
JWT login ──► roles attached to every request
    │
    ├─► GET /api/policies     → PolicyCatalog filters by allowedRoles
    │
    ├─► POST /api/agent/ask   → AgentAskHandler
    │       │
    │       ├─ escalate intent? → CreateTicketTool.Propose() → PendingApprovalStore
    │       │                      (Supervisor/Admin approves → TicketStore)
    │       │
    │       └─ otherwise → AskQuestionHandler (RAG pipeline)
    │               │
    │               1. Embed question
    │               2. Search vector store (role-filtered)
    │               3. No hits? → refuse
    │               4. Hits? → grounded answer + citations
    │
    └─► GET /health           → status, AI mode, index size, pending approvals
```

Policies live as markdown files in `data/policies/` with YAML frontmatter (`title`, `allowedRoles`). On startup the API ingests them into an in-memory vector index.

---

## Architecture overview

```text
┌─────────────────────────────────────────────────────────────┐
│  Presentation                                               │
│  web/ (React)          src/Api/Program.cs (Minimal API)     │
└───────────────────────────────┬─────────────────────────────┘
                                │
┌───────────────────────────────▼─────────────────────────────┐
│  Application (use cases / orchestration)                    │
│  AskQuestionHandler · AgentAskHandler · IngestService       │
│  AskQuestionValidator · EscalateIntentDetector                │
└───────────────────────────────┬─────────────────────────────┘
                                │
┌───────────────────────────────▼─────────────────────────────┐
│  Domain (rules & models)                                    │
│  PolicyDocument · PolicyChunk · PendingApproval · TicketDraft │
│  CreateTicketTool · role visibility rules                     │
└───────────────────────────────┬─────────────────────────────┘
                                │
┌───────────────────────────────▼─────────────────────────────┐
│  Infrastructure                                             │
│  InMemoryVectorStore · PolicyCatalog · TokenService         │
│  Lexical/OpenAI clients · AiCallLogger · TicketStore        │
└─────────────────────────────────────────────────────────────┘
```

Code is organized by **feature folders** under `src/Api/Features/` (Ask, Agent, Auth, Policies, Rag, Logging). That keeps related logic together while still separating concerns — a practical take on Clean Architecture in a single deployable API.

---

## Clean Architecture (how this repo applies it)

| Layer | Responsibility | Examples in this repo |
| --- | --- | --- |
| **Presentation** | HTTP, UI, auth wiring | `Program.cs` endpoints, `web/src/App.tsx` |
| **Application** | Workflows, validation, orchestration | `AskQuestionHandler`, `AgentAskHandler`, `AskQuestionValidator` |
| **Domain** | Business rules and core models | `PolicyDocument.IsVisibleTo()`, `CreateTicketTool.Propose()` |
| **Infrastructure** | Files, AI APIs, storage, logging | `PolicyCatalog`, `InMemoryVectorStore`, `AiClientFactory` |

**Dependency rule:** application code depends on abstractions (`IEmbeddingClient`, `IGroundedChatClient`, `IAiCallLogger`), not on Azure/OpenAI SDK types directly. Endpoints stay thin — they validate input, read claims, and call handlers.

---

## SOLID principles (with examples)

### Single Responsibility (S)
Each class has one reason to change:

- `AskQuestionValidator` — only validates question input
- `CreateTicketTool` — only builds a ticket **draft** (never saves it)
- `AskQuestionHandler` — only runs the RAG Q&A pipeline
- `RequestLoggingMiddleware` — only logs HTTP method, path, status, duration

### Open/Closed (O)
Add a new AI provider without editing handlers:

```csharp
// AiClientFactory picks implementation from config
return (new LexicalEmbeddingClient(), new LexicalGroundedChatClient(), "Lexical");
// or OpenAiEmbeddingClient + OpenAiGroundedChatClient when keys are set
```

Handlers depend on `IEmbeddingClient` and `IGroundedChatClient` — open for extension, closed for modification.

### Liskov Substitution (L)
`LexicalEmbeddingClient` and `OpenAiEmbeddingClient` both implement `IEmbeddingClient`. Either can be injected; `AskQuestionHandler` behaves correctly with both.

### Interface Segregation (I)
AI concerns are split into small interfaces instead of one “do everything” service:

- `IEmbeddingClient` — turn text into vectors
- `IGroundedChatClient` — answer using retrieved chunks
- `IAiCallLogger` — audit AI operations

### Dependency Inversion (D)
High-level handlers depend on abstractions registered in `Program.cs`:

```csharp
public sealed class AskQuestionHandler(
    InMemoryVectorStore store,
    IEmbeddingClient embeddings,      // abstraction
    IGroundedChatClient chat,         // abstraction
    IAiCallLogger aiLog,              // abstraction
    IOptions<RagOptions> ragOptions)
```

---

## Design patterns (with examples)

| Pattern | Where | Why |
| --- | --- | --- |
| **Factory** | `AiClientFactory.Create()` | Central place to construct the right AI client from configuration |
| **Strategy** | `LexicalGroundedChatClient` vs `OpenAiGroundedChatClient` | Swap answer-generation strategy at runtime |
| **Handler / use case** | `AskQuestionHandler`, `AgentAskHandler` | One class per application workflow |
| **Middleware** | `RequestLoggingMiddleware` | Cross-cutting HTTP logging without cluttering endpoints |
| **Options** | `RagOptions`, `AgentOptions` via `IOptions<T>` | Typed, validated config (`TopK`, `MaxSteps`, etc.) |
| **Composition** | `AgentAskHandler` uses `AskQuestionHandler` | Agent path reuses RAG instead of duplicating it |
| **Human-in-the-loop (HITL)** | Propose → `PendingApprovalStore` → Approve/Reject | Write tools never execute without explicit approval |

**Example — Strategy + Factory together:**

```csharp
// Program.cs registers whatever AiClientFactory returns
var (embedClient, chatClient, aiMode) = AiClientFactory.Create(builder.Configuration);
builder.Services.AddSingleton(embedClient);
builder.Services.AddSingleton(chatClient);
```

**Example — ACL at retrieve time (security pattern):**

```csharp
// InMemoryVectorStore.Search — filter by role BEFORE scoring
snapshot = _chunks.Where(c => c.IsVisibleTo(roles)).ToList();
```

---

## Project structure

```text
contoso-policy-assistant/
├── data/policies/          # Markdown policies + allowedRoles frontmatter
├── src/Api/                # .NET 8 Web API
│   ├── Features/
│   │   ├── Ask/            # RAG question handler + validator
│   │   ├── Agent/          # Escalation agent + ticket approval
│   │   ├── Auth/           # JWT login (dev users)
│   │   ├── Policies/       # Policy catalog loader
│   │   ├── Rag/            # Ingest, vector store, AI clients
│   │   └── Logging/        # Request + AI call logging
│   └── Program.cs          # Composition root + endpoints
├── web/                    # React + Vite UI
│   └── e2e/                # Playwright smoke tests
├── tests/                  # xUnit unit + golden eval tests
├── evals/golden.json       # Expected behaviors for AI paths
└── docs/                   # PRD, ADR, architecture notes, demo script
```

---

## Quick start

### Option A — Local (API + UI)

```bash
# Terminal 1 — API
cd src/Api && dotnet run

# Terminal 2 — Web
cd web && npm install && npm run dev
```

| Service | URL |
| --- | --- |
| API health | http://localhost:5080/health |
| Swagger (dev) | http://localhost:5080/swagger |
| UI | http://localhost:5173 |

### Option B — Docker (API only)

```bash
docker compose up --build
# API on http://localhost:5080
cd web && npm run dev   # UI still runs locally
```

### Demo users (password: `pass`)

| Username | Roles |
| --- | --- |
| `alice` | Employee |
| `bob` | Supervisor, Employee |
| `admin` | Admin, Supervisor, Employee |

No AI API key is required — default provider is **`Lexical`** (offline keyword matching). Set Azure OpenAI or OpenAI in config / user-secrets to use real embeddings — see `.env.example`.

---

## Try these in the UI

| Action | User | Expected result |
| --- | --- | --- |
| View policies | Alice | 4 policies — no “Workplace Safety Escalation” |
| View policies | Bob | All 5 policies including safety SOP |
| Ask about leave days | Alice | Grounded answer citing Leave Policy (20 days) |
| Ask about cafeteria | Alice | Refusal — not in policy docs |
| Escalate + create ticket | Bob | Pending approval draft → Approve → ticket created |
| Escalate + create ticket | Alice | Blocked — Employees cannot propose tickets |

Walkthrough: [docs/DEMO-SCRIPT.md](docs/DEMO-SCRIPT.md)

---

## Testing

```powershell
# Backend unit + golden eval tests
dotnet test

# Golden eval script (same cases as CI)
.\scripts\run-evals.ps1

# Frontend build
cd web && npm run build

# End-to-end (starts Docker API + Vite if configured)
cd web && npm run test:e2e
```

Golden evals in `evals/golden.json` guard against regressions in grounding, refusal, ACL, and ticket approval rules.

---

## Configuration

| Setting | Purpose | Default |
| --- | --- | --- |
| `Ai:Provider` | `Lexical`, `AzureOpenAI`, or `OpenAI` | `Lexical` |
| `Policies:RootPath` | Folder with `*.md` policies | `../../data/policies` |
| `Rag:TopK` | Chunks retrieved per question | `4` |
| `Rag:AutoIngestOnStartup` | Index policies on startup | `true` |
| `Agent:MaxSteps` | Agent loop limit | `4` |

Secrets (JWT key, API keys) belong in user-secrets or environment variables — never in the frontend.

---

## Observability

| Today | Production equivalent |
| --- | --- |
| `RequestLoggingMiddleware` | APM / request telemetry |
| `logs/ai-*.jsonl` (redacted) | Centralized AI audit logs |
| `/health` (aiMode, chunks, tickets) | Health probe for containers |

---

## Documentation

| Doc | Description |
| --- | --- |
| [docs/PRD.md](docs/PRD.md) | Problem statement and success metrics |
| [docs/ADR-001-rag-vs-finetune.md](docs/ADR-001-rag-vs-finetune.md) | Why RAG instead of fine-tuning |
| [docs/AZURE-TARGET-ARCHITECTURE.md](docs/AZURE-TARGET-ARCHITECTURE.md) | Optional cloud deployment shape |
| [docs/DEMO-SCRIPT.md](docs/DEMO-SCRIPT.md) | Step-by-step demo walkthrough |
| [docs/RISKS.md](docs/RISKS.md) | Known risks and mitigations |
| [docs/WBS.md](docs/WBS.md) | Delivery work breakdown |

---

## CI

GitHub Actions (`.github/workflows/ci.yml`): `dotnet test` · `npm run build` · `docker build`

---

## License & use

Sample / learning project. Fork it, swap the policies, plug in your identity provider, and point the vector store at Azure AI Search or another backend — the handler contracts and security patterns stay the same.
