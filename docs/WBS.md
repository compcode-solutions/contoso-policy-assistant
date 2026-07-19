# WBS — Contoso Policy Assistant (pilot)

Effort bands are consulting-style estimates for a 1–2 engineer pilot.

## 1. Discovery & framing
| ID | Work package | Outcome |
| --- | --- | --- |
| 1.1 | Stakeholder goals / success metrics | PRD, golden questions |
| 1.2 | Role model (Employee / Supervisor / Admin) | ACL rules |
| 1.3 | Source-of-truth for SOPs | Markdown corpus + owners |

## 2. Ingest
| ID | Work package | Outcome |
| --- | --- | --- |
| 2.1 | Frontmatter schema (`title`, `allowedRoles`) | Consistent metadata |
| 2.2 | Chunking (~word window) | Retrievable passages |
| 2.3 | Embed + store vectors | Searchable index |
| 2.4 | Re-ingest command / startup ingest | Operable refresh |

## 3. RAG ask path
| ID | Work package | Outcome |
| --- | --- | --- |
| 3.1 | AuthN/Z (dev JWT → Entra later) | Protected APIs |
| 3.2 | Role-filtered retrieval | No ACL leaks |
| 3.3 | Grounded answer + citations / refusal | Trustworthy UX |
| 3.4 | Golden evals in CI | Regression gate |

## 4. Agent + HITL
| ID | Work package | Outcome |
| --- | --- | --- |
| 4.1 | Escalate intent detection | Tool routing |
| 4.2 | `create_ticket` propose-only | No auto-write |
| 4.3 | Approve / Reject APIs + UI | Human gate |
| 4.4 | AI/tool call logging | Audit trail |

## 5. Harden & handoff
| ID | Work package | Outcome |
| --- | --- | --- |
| 5.1 | Docker + CI | Repeatable build |
| 5.2 | Azure target architecture + risks | Customer conversation |
| 5.3 | Observability mapping (logs → App Insights) | Ops story |
| 5.4 | Demo script + resume evidence | Interview / stakeholder demo |

## Dependency sketch

```text
Discovery → Ingest → RAG → Agent/HITL → Harden
                ↘ Evals/CI ↗
```
