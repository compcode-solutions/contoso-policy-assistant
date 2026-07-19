# 5-minute demo script

Optional: record with Loom using this outline.

## Prep (before recording)
```bash
cd src/Api && dotnet run
cd web && npm run dev
```
Or: `docker compose up --build` (API on :5080) + local web.

## Minute 0:00–0:40 — Frame
> “Contoso Policy Assistant — grounded policy Q&A with role ACL and an approval-gated escalation ticket. Built as a GCID-style vertical slice: .NET 8 + React + RAG.”

Show: PRD one-liner + architecture sketch (RAG filter → answer; agent propose → approve).

## Minute 0:40–1:40 — Auth + ACL
1. Sign in as **Alice (Employee)**.
2. Show policy list — **no** Workplace Safety Escalation.
3. Sign out → **Bob (Supervisor)** — escalate SOP visible.

> “In Azure this is Entra app roles + AI Search security trimming.”

## Minute 1:40–3:10 — Grounded RAG
As Alice:
1. Ask: *How many leave days do I get each year?* → cited answer.
2. Ask: *What's the cafeteria menu?* → **I don’t know** / not grounded.
3. Ask safety escalation question → **no Priority-1 leak**.

> “No context, no answer. ACL at retrieve time.”

## Minute 3:10–4:30 — Agent HITL
As Bob:
1. Ask: *Escalate this Priority-1 safety incident at Dock 4 — create a ticket*
2. Show **pending approval** draft — emphasize ticket list still empty.
3. Click **Approve** → ticket created.
4. (Optional) Reject path in a second take.

> “Write tools need human gates.”

## Minute 4:30–5:00 — Close
- Point to `evals/golden.json` + CI.
- Point to `docs/AZURE-TARGET-ARCHITECTURE.md`.
- One line: “Next production step: Entra + AI Search + App Insights, same contracts.”

## Cold-start checklist (clone → demo)
1. Clone repo  
2. `dotnet test` (no secrets needed — Lexical)  
3. `dotnet run` in `src/Api` + `npm run dev` in `web`  
4. Login → ask → escalate → approve  
5. Optional: set Azure OpenAI user-secrets, restart, re-ask  
