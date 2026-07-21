import { expect, test, type Page } from "@playwright/test";

async function waitForApiConnected(page: Page) {
  await expect(page.getByText(/Connected —/)).toBeVisible({ timeout: 30_000 });
}

async function signInAs(page: Page, label: RegExp) {
  await page.getByRole("button", { name: label }).click();
  await expect(page.getByText(/Signed in as/)).toBeVisible();
}

async function askQuestion(page: Page, question: string) {
  await page.getByLabel("Your question").fill(question);
  await page.getByRole("button", { name: "Ask agent" }).click();
  await expect(page.getByRole("button", { name: "Ask agent" })).toBeEnabled({
    timeout: 30_000,
  });
}

test.beforeEach(async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Contoso Policy Assistant" })).toBeVisible();
  await waitForApiConnected(page);
});

test("home shows API health when backend is up", async ({ page }) => {
  await expect(page.getByRole("heading", { name: "API status" })).toBeVisible();
  await expect(page.getByText(/Connected —/)).toContainText("Contoso.PolicyAssistant");
  await expect(page.getByRole("button", { name: /Alice · Employee/ })).toBeEnabled();
});

test("Alice: ACL hides safety policy, RAG cites leave, cafeteria refuses", async ({
  page,
}) => {
  await signInAs(page, /Alice · Employee/);

  await expect(page.getByText(/policies visible for your roles/)).toBeVisible();
  await expect(page.getByText("Leave Policy")).toBeVisible();
  await expect(page.getByText("Workplace Safety Escalation")).toHaveCount(0);

  await page.getByRole("button", { name: "Leave days (RAG answer)" }).click();
  await page.getByRole("button", { name: "Ask agent" }).click();
  await expect(page.getByRole("heading", { name: /Grounded answer/ })).toBeVisible({
    timeout: 30_000,
  });
  await expect(page.locator(".answer")).toContainText(/20/);
  await expect(page.getByRole("heading", { name: "Citations" })).toBeVisible();
  await expect(page.locator(".citations")).toContainText("Leave Policy");

  await page.getByRole("button", { name: "Cafeteria menu (refuse)" }).click();
  await page.getByRole("button", { name: "Ask agent" }).click();
  await expect(page.locator(".result")).toBeVisible({ timeout: 30_000 });
  await expect(page.locator(".answer")).toContainText(/don.?t know|not enough|cannot|refuse|grounded/i);
});

test("Bob: escalate proposes ticket, approve writes it", async ({ page }) => {
  await signInAs(page, /Bob · Supervisor/);

  await expect(page.getByText("Workplace Safety Escalation")).toBeVisible();

  await page.getByRole("button", { name: "Escalate + create ticket (HITL)" }).click();
  await page.getByRole("button", { name: "Ask agent" }).click();

  await expect(page.getByRole("heading", { name: /Pending approval/ })).toBeVisible({
    timeout: 30_000,
  });
  await expect(page.getByRole("heading", { name: /Tool: create_ticket/ })).toBeVisible();
  await expect(page.getByRole("button", { name: "Approve" })).toBeVisible();

  await page.getByRole("button", { name: "Approve" }).click();
  await expect(page.getByText(/Ticket created:/)).toBeVisible({ timeout: 15_000 });
  const ticketsSection = page
    .getByRole("heading", { name: "Created tickets" })
    .locator("..");
  await expect(ticketsSection).toBeVisible();
  await expect(ticketsSection).toContainText(/Safety escalation/i);
});
