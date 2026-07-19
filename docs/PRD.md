# Contoso Policy Assistant — PRD (Day 0)

## Problem
Employees waste time searching SOPs. Supervisors need a safe way to escalate incidents with an audit trail. Wrong AI answers are worse than no answer.

## Users
- **Employee** — ask general policy questions
- **Supervisor** — same + safety escalation SOP + approve tickets
- **Admin** — full access (later)

## Success metrics (pilot)
- ≥80% of golden eval questions grounded or correctly refused
- Zero ACL leaks (employee never sees supervisor-only docs)
- Ticket creation never happens without human approval
- Demo under 5 minutes

## In scope (Days 0–5)
- Ask UI + API
- JWT role stand-in for Entra
- Local RAG with citations/refusal
- Approval-gated ticket tool
- CI + Docker + Azure target architecture notes

## Out of scope
- Real ServiceNow / SAP connectors
- Fine-tuning models
- Multi-language UI
- Production Entra app registration (documented mapping only)

## Non-goals
- “ChatGPT with company vibe” without grounding
- Auto-sending tickets without approval
