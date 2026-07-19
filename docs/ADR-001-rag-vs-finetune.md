# ADR-001 — RAG over fine-tuning for Contoso Policy Assistant

## Status
Accepted (Day 5 wrap)

## Context
We need employees and supervisors to ask questions over Contoso SOPs with:
- citations (or explicit refusal),
- role-based document access,
- frequent policy edits,
- low tolerance for invented answers.

## Decision
Use **retrieval-augmented generation (RAG)** with role-filtered retrieval.  
Do **not** fine-tune a model on Contoso policies for this product slice.

## Rationale
| Concern | RAG | Fine-tune |
| --- | --- | --- |
| Policy updates | Re-ingest / re-index | Retrain / redeploy |
| Citations | Natural (chunk IDs) | Hard without retrieval |
| ACL / security trimming | Filter at retrieve time | Easy to leak in weights |
| Cost / time for pilot | Hours | Days–weeks |
| Hallucination control | Refuse when no hits | Still can invent |

Fine-tuning does not replace access control. Even a tuned model must not see supervisor-only SOPs for employees.

## Consequences
- Index freshness and chunk quality become product concerns.
- Eval suite (`evals/golden.json`) must guard groundedness + ACL leaks.
- Azure target: Azure OpenAI + Azure AI Search (not a custom trained weights pipeline).

## What we built (accurate scope)
- Local/in-memory vectors + `allowedRoles` metadata
- Providers: Lexical (offline), Azure OpenAI, OpenAI
- Agent write tool `create_ticket` is **approval-gated** (HITL), separate from RAG
