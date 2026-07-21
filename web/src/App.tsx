import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import "./App.css";

const API_BASE =
  import.meta.env.VITE_API_BASE_URL?.toString() || "http://localhost:5080";

const TOKEN_KEY = "cpa_access_token";
const SESSION_KEY = "cpa_session";

type Health = {
  status: string;
  service: string;
  utc: string;
  aiMode?: string;
  indexChunks?: number;
  tickets?: number;
  pendingApprovals?: number;
};

type Session = {
  accessToken: string;
  displayName: string;
  username: string;
  roles: string[];
};

type Citation = {
  n: number;
  title: string;
  excerpt: string;
};

type PendingApproval = {
  id: string;
  tool: string;
  requiresApproval: boolean;
  title: string;
  body: string;
  severity: string;
  requestedBy: string;
  createdUtc: string;
  status: string;
};

type AgentOk = {
  status: string;
  answer: string;
  citations: Citation[];
  grounded: boolean;
  question: string;
  callerRoles: string[];
  stepsUsed: number;
  phase: string;
  pendingApproval?: PendingApproval | null;
  note: string;
};

type PolicyRow = {
  id: string;
  title: string;
  allowedRoles: string[];
};

type TicketRow = {
  id: string;
  title: string;
  severity: string;
  approvedBy: string;
  createdUtc: string;
};

type ProblemDetails = {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
};

function loadSession(): Session | null {
  try {
    const raw = sessionStorage.getItem(SESSION_KEY);
    if (!raw) return null;
    return JSON.parse(raw) as Session;
  } catch {
    return null;
  }
}

function canApprove(session: Session | null) {
  return (
    !!session &&
    (session.roles.includes("Supervisor") || session.roles.includes("Admin"))
  );
}

export default function App() {
  const [health, setHealth] = useState<Health | null>(null);
  const [healthError, setHealthError] = useState<string | null>(null);

  const [session, setSession] = useState<Session | null>(() => loadSession());
  const [loginError, setLoginError] = useState<string | null>(null);
  const [loggingIn, setLoggingIn] = useState(false);

  const [policies, setPolicies] = useState<PolicyRow[]>([]);
  const [policiesMeta, setPoliciesMeta] = useState<string | null>(null);

  const [question, setQuestion] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [result, setResult] = useState<AgentOk | null>(null);
  const [fieldError, setFieldError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [actionMsg, setActionMsg] = useState<string | null>(null);
  const [tickets, setTickets] = useState<TicketRow[]>([]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const res = await fetch(`${API_BASE}/health`);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const data = (await res.json()) as Health;
        if (!cancelled) {
          setHealth(data);
          setHealthError(null);
        }
      } catch (e) {
        if (!cancelled) {
          setHealth(null);
          setHealthError(
            e instanceof Error
              ? e.message
              : "Could not reach API. Is it running on :5080?",
          );
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!session) {
      setPolicies([]);
      setPoliciesMeta(null);
      setTickets([]);
      return;
    }

    let cancelled = false;
    (async () => {
      const res = await fetch(`${API_BASE}/api/policies`, {
        headers: { Authorization: `Bearer ${session.accessToken}` },
      });
      if (cancelled) return;
      if (res.status === 401) {
        persistSession(null);
        return;
      }
      if (!res.ok) {
        setPoliciesMeta("Could not load policies");
        return;
      }
      const data = (await res.json()) as {
        totalInCatalog: number;
        visibleCount: number;
        policies: PolicyRow[];
      };
      setPolicies(data.policies);
      setPoliciesMeta(
        `${data.visibleCount} of ${data.totalInCatalog} policies visible for your roles`,
      );

      if (canApprove(session)) {
        const tRes = await fetch(`${API_BASE}/api/tickets`, {
          headers: { Authorization: `Bearer ${session.accessToken}` },
        });
        if (!cancelled && tRes.ok) {
          setTickets((await tRes.json()) as TicketRow[]);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [session]);

  function persistSession(next: Session | null) {
    setSession(next);
    if (next) {
      sessionStorage.setItem(TOKEN_KEY, next.accessToken);
      sessionStorage.setItem(SESSION_KEY, JSON.stringify(next));
    } else {
      sessionStorage.removeItem(TOKEN_KEY);
      sessionStorage.removeItem(SESSION_KEY);
    }
  }

  function logout() {
    persistSession(null);
    setResult(null);
    setLoginError(null);
    setActionMsg(null);
  }

  async function loginAs(username: string, password = "pass") {
    setLoggingIn(true);
    setLoginError(null);
    try {
      const res = await fetch(`${API_BASE}/api/auth/login`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ username, password }),
      });
      if (res.status === 401) {
        setLoginError("Invalid credentials");
        return;
      }
      if (!res.ok) {
        setLoginError(`Login failed (HTTP ${res.status})`);
        return;
      }
      const data = (await res.json()) as {
        accessToken: string;
        displayName: string;
        username: string;
        roles: string[];
      };
      persistSession({
        accessToken: data.accessToken,
        displayName: data.displayName,
        username: data.username,
        roles: data.roles,
      });
    } catch {
      setLoginError("Network error — is the API running?");
    } finally {
      setLoggingIn(false);
    }
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    if (!session) return;

    setSubmitting(true);
    setResult(null);
    setFieldError(null);
    setFormError(null);
    setActionMsg(null);

    try {
      const res = await fetch(`${API_BASE}/api/agent/ask`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${session.accessToken}`,
        },
        body: JSON.stringify({ question }),
      });

      if (res.status === 401) {
        setFormError("Session expired — sign in again.");
        logout();
        return;
      }

      if (res.status === 400) {
        const problem = (await res.json()) as ProblemDetails;
        const qErr =
          problem.errors?.question?.[0] ?? problem.errors?.Question?.[0];
        setFieldError(qErr ?? problem.title ?? "Validation failed");
        return;
      }

      if (!res.ok) {
        setFormError(`Request failed (HTTP ${res.status})`);
        return;
      }

      const data = (await res.json()) as AgentOk;
      setResult(data);
    } catch {
      setFormError("Network error — is the API running?");
    } finally {
      setSubmitting(false);
    }
  }

  async function resolveApproval(kind: "approve" | "reject") {
    if (!session || !result?.pendingApproval) return;
    setActionMsg(null);
    const id = result.pendingApproval.id;
    const res = await fetch(`${API_BASE}/api/agent/${kind}/${id}`, {
      method: "POST",
      headers: { Authorization: `Bearer ${session.accessToken}` },
    });

    if (res.status === 403) {
      setActionMsg("Only Supervisor/Admin can approve or reject.");
      return;
    }
    if (!res.ok) {
      setActionMsg(`${kind} failed (HTTP ${res.status})`);
      return;
    }

    const body = await res.json();
    if (kind === "approve") {
      setActionMsg(`Ticket created: ${body.ticketId} (${body.severity})`);
      setTickets((prev) => [
        {
          id: body.ticketId,
          title: body.title,
          severity: body.severity,
          approvedBy: body.approvedBy,
          createdUtc: body.createdUtc,
        },
        ...prev,
      ]);
    } else {
      setActionMsg("Draft rejected — no ticket written.");
    }

    setResult({
      ...result,
      status: kind === "approve" ? "answered" : "answered",
      pendingApproval: result.pendingApproval
        ? { ...result.pendingApproval, status: kind === "approve" ? "approved" : "rejected" }
        : null,
      note:
        kind === "approve"
          ? "Human approved — ticket written."
          : "Human rejected — zero side effects.",
    });
  }

  return (
    <div className="page">
      <header className="header">
        <p className="eyebrow">Policy assistant</p>
        <h1>Contoso Policy Assistant</h1>
        <p className="lede">
          Ask questions about company policies and get grounded answers with citations.
          Escalations require supervisor approval before a ticket is created.
        </p>
        {session ? (
          <p className="session-bar">
            Signed in as <strong>{session.displayName}</strong>
            <span className="roles"> · {session.roles.join(", ")}</span>
            <button type="button" className="linkish logout" onClick={logout}>
              Sign out
            </button>
          </p>
        ) : null}
      </header>

      <section className="card">
        <h2>API status</h2>
        {health ? (
          <p className="ok">
            Connected — <code>{health.service}</code>
            {health.aiMode ? (
              <>
                {" "}
                · AI <code>{health.aiMode}</code>
              </>
            ) : null}
            {typeof health.indexChunks === "number" ? (
              <>
                {" "}
                · index <code>{health.indexChunks}</code>
              </>
            ) : null}
            {typeof health.pendingApprovals === "number" ? (
              <>
                {" "}
                · pending <code>{health.pendingApprovals}</code>
              </>
            ) : null}
          </p>
        ) : (
          <p className="err">
            {healthError ?? "Checking…"}
            <span className="hint">
              Run <code>dotnet run</code> in <code>src/Api</code>
            </span>
          </p>
        )}
      </section>

      {!session ? (
        <section className="card">
          <h2>Sign in (dev JWT)</h2>
          <p className="meta">
            Password: <code>pass</code>. Use <strong>Bob</strong> to propose +
            approve escalation tickets.
          </p>
          <div className="login-row">
            <button
              type="button"
              disabled={loggingIn || !health}
              onClick={() => loginAs("alice")}
            >
              Alice · Employee
            </button>
            <button
              type="button"
              disabled={loggingIn || !health}
              onClick={() => loginAs("bob")}
            >
              Bob · Supervisor
            </button>
            <button
              type="button"
              disabled={loggingIn || !health}
              onClick={() => loginAs("admin")}
            >
              Ada · Admin
            </button>
          </div>
          {loginError ? <p className="err">{loginError}</p> : null}
        </section>
      ) : (
        <>
          <section className="card">
            <h2>Policies you can access</h2>
            {policiesMeta ? <p className="meta">{policiesMeta}</p> : null}
            <ul className="policy-list">
              {policies.map((p) => (
                <li key={p.id}>
                  <strong>{p.title}</strong>
                  <span className="meta"> — {p.allowedRoles.join(", ")}</span>
                </li>
              ))}
            </ul>
          </section>

          <section className="card">
            <h2>Ask (agent)</h2>
            <form className="ask-form" onSubmit={onSubmit}>
              <label htmlFor="question">Your question</label>
              <textarea
                id="question"
                rows={4}
                value={question}
                onChange={(e) => setQuestion(e.target.value)}
                placeholder="e.g. Escalate this Priority-1 safety incident at Dock 4"
                disabled={submitting}
              />
              {fieldError ? <p className="field-error">{fieldError}</p> : null}
              {formError ? <p className="err">{formError}</p> : null}
              <button type="submit" disabled={submitting || !health}>
                {submitting ? "Working…" : "Ask agent"}
              </button>
            </form>

            {result ? (
              <div className="result">
                <h3>
                  {result.status === "pendingApproval"
                    ? "Pending approval"
                    : result.status === "forbiddenTool"
                      ? "Tool blocked"
                      : result.grounded
                        ? "Grounded answer"
                        : "Response"}{" "}
                  <span
                    className={
                      result.status === "pendingApproval"
                        ? "badge warn-badge"
                        : result.grounded
                          ? "badge ok-badge"
                          : "badge warn-badge"
                    }
                  >
                    {result.status}
                  </span>
                </h3>
                <p className="answer">{result.answer}</p>

                {result.pendingApproval &&
                result.pendingApproval.status === "pending" ? (
                  <div className="approval-box">
                    <h4>
                      Tool: {result.pendingApproval.tool}{" "}
                      <span className="meta">
                        requiresApproval=
                        {String(result.pendingApproval.requiresApproval)}
                      </span>
                    </h4>
                    <p>
                      <strong>
                        [{result.pendingApproval.severity}]{" "}
                        {result.pendingApproval.title}
                      </strong>
                    </p>
                    <pre className="ticket-body">{result.pendingApproval.body}</pre>
                    {canApprove(session) ? (
                      <div className="login-row">
                        <button type="button" onClick={() => resolveApproval("approve")}>
                          Approve
                        </button>
                        <button type="button" onClick={() => resolveApproval("reject")}>
                          Reject
                        </button>
                      </div>
                    ) : (
                      <p className="hint">
                        Sign in as Bob/Ada to approve or reject this draft.
                      </p>
                    )}
                  </div>
                ) : null}

                {actionMsg ? <p className="ok">{actionMsg}</p> : null}

                {result.citations.length > 0 ? (
                  <div className="citations">
                    <h4>Citations</h4>
                    <ol>
                      {result.citations.map((c) => (
                        <li key={c.n}>
                          <strong>
                            [{c.n}] {c.title}
                          </strong>
                          <p className="meta">{c.excerpt}</p>
                        </li>
                      ))}
                    </ol>
                  </div>
                ) : null}
                <p className="meta">
                  {result.phase} · steps {result.stepsUsed} ·{" "}
                  {result.callerRoles.join(", ")}
                </p>
                <p className="meta">{result.note}</p>
              </div>
            ) : null}
          </section>

          {canApprove(session) && tickets.length > 0 ? (
            <section className="card">
              <h2>Created tickets</h2>
              <ul className="policy-list">
                {tickets.map((t) => (
                  <li key={t.id}>
                    <strong>
                      [{t.severity}] {t.title}
                    </strong>
                    <span className="meta">
                      {" "}
                      — approved by {t.approvedBy}
                    </span>
                  </li>
                ))}
              </ul>
            </section>
          ) : null}

          <section className="card">
            <h2>Try these</h2>
            <ul className="samples">
              <li>
                <button
                  type="button"
                  className="linkish"
                  onClick={() =>
                    setQuestion("How many leave days do I get each year?")
                  }
                >
                  Leave days (RAG answer)
                </button>
              </li>
              <li>
                <button
                  type="button"
                  className="linkish"
                  onClick={() =>
                    setQuestion(
                      "Escalate this Priority-1 safety incident at Dock 4 — create a ticket",
                    )
                  }
                >
                  Escalate + create ticket (HITL)
                </button>
              </li>
              <li>
                <button
                  type="button"
                  className="linkish"
                  onClick={() =>
                    setQuestion("What's the cafeteria menu for Friday?")
                  }
                >
                  Cafeteria menu (refuse)
                </button>
              </li>
            </ul>
          </section>
        </>
      )}
    </div>
  );
}
