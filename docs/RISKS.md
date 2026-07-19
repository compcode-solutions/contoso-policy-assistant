# Risks & mitigations

| Risk | Impact | Likelihood (lab) | Mitigation |
| --- | --- | --- | --- |
| Hallucinated policy answers | High (compliance) | Med without grounding | Refuse when no/low-relevance hits; citations required for grounded=true |
| ACL leak (employee sees supervisor SOP) | Critical | Med if filter forgotten | Filter **at retrieve time**; golden eval forbids safety citations for Employee |
| Auto-created tickets | High | Low (gated) | `create_ticket` always `requiresApproval=true`; Approve endpoint separate |
| Prompt/tool injection via question text | Med | Med | No free-form tool JSON from model in lab; rule-based escalate intent; HITL before write |
| Secrets in repo / frontend | High | Low | User-secrets / env; `.gitignore`; never ship keys to Vite |
| Stale index after policy edit | Med | High in ops | Re-ingest (`POST /api/ingest` Admin); Azure: indexer schedule |
| Lexical offline mode weaker than embeddings | Med (demo quality) | High without keys | Document Lexical as lab default; switch to Azure OpenAI for interview realism |
| In-memory store lost on restart | Med | Certain in container | Accept for lab; Azure AI Search for durability |
| Eval false confidence | Med | Med | Keep golden set small but real; expand before production |
| Cost overrun on Azure OpenAI | Med | Med | Budgets/alerts; small deployments; cache embeddings where safe |

## Interview phrasing
“Biggest risk isn’t the model — it’s **retrieval without ACL** and **write tools without a human gate**. We designed for both first.”
