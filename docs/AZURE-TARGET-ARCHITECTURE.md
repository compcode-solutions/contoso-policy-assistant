# Azure target architecture

Maps the lab we built to a customer-ready Azure shape. Boxes below are the talking points.

```text
                         ┌─────────────────┐
                         │  Entra ID       │
                         │  (app roles)    │
                         └────────┬────────┘
                                  │ OIDC / MSAL
┌──────────────┐     ┌────────────▼────────────┐     ┌──────────────────┐
│ Azure        │     │ App Service /           │     │ Azure OpenAI     │
│ Front Door   │────▶│ Container Apps (API)    │────▶│ chat + embed     │
│ (+ WAF)      │     │ + Static Web Apps (UI)  │     └──────────────────┘
└──────────────┘     └────────────┬────────────┘
                                  │
                     ┌────────────▼────────────┐
                     │ Azure AI Search         │
                     │ vectors + allowedRoles  │
                     │ security trimming       │
                     └────────────┬────────────┘
                                  │
                     ┌────────────▼────────────┐
                     │ Key Vault               │
                     │ AOAI keys, JWT secrets  │
                     └─────────────────────────┘

                     ┌─────────────────────────┐
                     │ Azure Monitor /         │
                     │ App Insights            │
                     │ (requests + AI traces)  │
                     └─────────────────────────┘

Write path (HITL):
  API propose create_ticket → Approval UX → Logic Apps / ServiceNow connector
```

## Lab → Azure mapping

| Lab component | Azure service |
| --- | --- |
| React Vite UI | Static Web Apps / App Service |
| .NET 8 API | App Service or Container Apps (see `Dockerfile`) |
| Dev JWT login | Entra ID + MSAL; API JWT bearer validation |
| `PolicyCatalog` + `allowedRoles` | AI Search index fields + filter/trim |
| `InMemoryVectorStore` | Azure AI Search vector index |
| Lexical / OpenAI clients | Azure OpenAI deployments |
| `logs/ai-*.jsonl` + request middleware | App Insights (+ optional Log Analytics) |
| `create_ticket` Approve/Reject | Logic Apps / ServiceNow with approval |
| GitHub Actions CI | Same pipeline → ACR + deploy slot |
| Secrets in user-secrets | Key Vault references |

## Non-goals (still)
- Real ServiceNow connector implementation
- Multi-region active-active
- Customer-trained foundation model

## Security notes for the interview
1. **Never** put API keys in the SPA.
2. Apply **ACL at retrieve**, not only in the prompt (“please don’t show…”).
3. Treat ticket creation as a **privileged tool** — human approval before side effects.
