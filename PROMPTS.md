# Prompt log (Cursor + AI)

Log accept / reject / edit decisions. This is interview evidence of process.

### Template
```
### Prompt N — YYYY-MM-DD HH:mm
**Pattern:** Specify | Iterate | Constrain | Critique
**Prompt:** …
**Result:** …
**Decision:** Accepted | Rejected | Edited
**Note:** …
```

### Prompt 1 — Day 0 scaffold
**Pattern:** Specify  
**Prompt:** Scaffold .NET 8 API + React Vite + policies + Cursor rules; health endpoint only; no AI packages yet.  
**Result:** Repo created with Api, Web, data/policies, docs.  
**Decision:** Accepted  
**Note:** Azure OpenAI preferred later; keys via env/user-secrets.

### Prompt 2 — Day 1 ask stub
**Pattern:** Specify  
**Prompt:** Vertical slice POST /api/questions with validation + React form + xUnit tests; no OpenAI packages.  
**Result:** Ask feature folder, ValidationProblem on empty/long input, UI field errors, tests green.  
**Decision:** Accepted  
**Note:** Stub echoes question; RAG replaces handler on Day 3.

### Prompt 3 — Day 2 JWT + ACL
**Pattern:** Specify + Constrain  
**Prompt:** Dev JWT auth with Employee/Supervisor roles; protect ask API; React stores token; policy frontmatter allowedRoles; no real Entra — document Azure mapping.  
**Result:** Login + PolicyCatalog filter + protected endpoints + UI role/session + tests.  
**Decision:** Accepted  
**Note:** Ask still stub; ACL preview feeds Day 3 retrieval filter.

### Prompt 4 — Day 3 RAG
**Pattern:** Specify + Constrain  
**Prompt:** Local RAG with role-filtered retrieval and citations; Azure OpenAI preferred, OpenAI fallback; never answer without context; secrets not committed.  
**Result:** Chunk/ingest/vector store + Ask grounded response + Lexical offline mode + UI citations + tests.  
**Decision:** Accepted (added Azure.AI.OpenAI package)  
**Note:** Default Provider=Lexical so demo works without keys; switch via user-secrets.

### Prompt 5 — Day 4 agent HITL + evals
**Pattern:** Specify + Constrain  
**Prompt:** Agent path with create_ticket gated by human approval; golden evals; AI call logging; max 4 steps; no auto-write.  
**Result:** Agent endpoints, Approve/Reject UI, golden.json + tests, ai-*.jsonl logger, tighter Lexical refusal.  
**Decision:** Accepted  
**Note:** Skipped full Semantic Kernel package; CreateTicketTool documents the plugin boundary.

### Prompt 6 — Day 5 Docker/CI/docs
**Pattern:** Specify + Constrain  
**Prompt:** Docker + GitHub Actions CI + consulting docs (ADR, WBS, Azure arch); do not refactor RAG; docs accurate to what we built.  
**Result:** Dockerfile/compose, CI workflow, request logging, ADR/WBS/RISKS/Azure/Demo docs, resume bullet, README wrap.  
**Decision:** Accepted  
**Note:** Qdrant compose profile is placeholder only — InMemoryVectorStore unchanged.
