const el = (id) => document.getElementById(id);

function formatDate(iso) {
  try {
    return new Date(iso).toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
  } catch {
    return iso;
  }
}

function formatDateTime(iso) {
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
}

function formatUsd(micros) {
  if (micros === null || micros === undefined) return "Unknown";
  return `$${(micros / 1_000_000).toFixed(4)}`;
}

function formatGbp(minor) {
  if (minor === null || minor === undefined) return "Unknown";
  return `£${(minor / 100).toFixed(2)}`;
}

function formatTokens(n) {
  return (n ?? 0).toLocaleString();
}

function statCard(title, value) {
  const section = document.createElement("section");
  section.className = "panel";
  const h2 = document.createElement("h2");
  h2.className = "panel-title";
  h2.textContent = title;
  const p = document.createElement("p");
  p.className = "stat-value";
  p.textContent = value;
  section.appendChild(h2);
  section.appendChild(p);
  return section;
}

function renderStats(container, entries) {
  container.innerHTML = "";
  entries.forEach(([title, value]) => container.appendChild(statCard(title, value)));
}

function table(headers, rows) {
  const t = document.createElement("table");
  t.className = "admin-table";
  const thead = document.createElement("thead");
  const headRow = document.createElement("tr");
  headers.forEach((h) => {
    const th = document.createElement("th");
    th.textContent = h;
    headRow.appendChild(th);
  });
  thead.appendChild(headRow);
  t.appendChild(thead);

  const tbody = document.createElement("tbody");
  rows.forEach((cells) => {
    const tr = document.createElement("tr");
    cells.forEach((cell) => {
      const td = document.createElement("td");
      if (cell instanceof Node) td.appendChild(cell);
      else td.textContent = cell;
      tr.appendChild(td);
    });
    tbody.appendChild(tr);
  });
  t.appendChild(tbody);
  return t;
}

// --- Tabs ---------------------------------------------------------------

function initTabs() {
  const tabs = document.querySelectorAll(".admin-tab");
  tabs.forEach((tab) => {
    tab.addEventListener("click", () => {
      tabs.forEach((t) => t.setAttribute("aria-selected", "false"));
      tab.setAttribute("aria-selected", "true");
      document.querySelectorAll(".admin-panel-section").forEach((s) => (s.hidden = true));
      el(`tab-${tab.dataset.tab}`).hidden = false;
      loadTab(tab.dataset.tab);
    });
  });
}

const loaded = new Set();

function loadTab(name) {
  if (loaded.has(name)) return;
  loaded.add(name);
  if (name === "overview") loadOverview();
  else if (name === "users") loadUsers();
  else if (name === "credits") loadCredits();
  else if (name === "plugins") loadPlugins();
  else if (name === "ai-usage") loadAiUsage();
  else if (name === "audit") loadAudit();
  else if (name === "revenue") loadRevenue();
  else if (name === "promotions") loadPromotions();
  else if (name === "system") loadSystem();
}

// --- Overview -------------------------------------------------------------

async function loadOverview() {
  const container = el("overviewStats");
  try {
    const response = await apiFetch("/api/admin/overview");
    if (!response.ok) throw new Error("Overview unavailable");
    const data = await response.json();
    renderStats(container, [
      ["Total users", data.totalUsers.toLocaleString()],
      ["Total credit balance", data.totalCreditBalance.toLocaleString()],
      ["Gross credits consumed", data.grossCreditsConsumed.toLocaleString()],
      ["Credits refunded", data.creditsRefunded.toLocaleString()],
      ["Plugin projects", data.totalPluginProjects.toLocaleString()],
      ["Saved versions", data.totalVersions.toLocaleString()],
      ["Standard builds", data.standardBuilds.toLocaleString()],
      ["Validated builds", data.validatedBuilds.toLocaleString()],
      ["AI requests", data.aiRequests.toLocaleString()],
      ["AI total tokens", formatTokens(data.aiTotalTokens)],
      ["AI estimated cost", formatUsd(data.aiEstimatedCostUsdMicros)],
      ["Active promotions", data.activePromotions.toLocaleString()],
      ["Promotional credits granted", data.promotionalCreditsGranted.toLocaleString()],
      ["Free builds redeemed", data.freeBuildsRedeemed.toLocaleString()],
    ]);
  } catch {
    container.innerHTML = "<p>Unavailable. Please refresh.</p>";
  }
}

// --- Credits ------------------------------------------------------------

async function loadCredits() {
  const container = el("creditStats");
  try {
    const response = await apiFetch("/api/admin/credits");
    if (!response.ok) throw new Error("Credit analytics unavailable");
    const data = await response.json();
    renderStats(container, [
      ["Total credits held", data.totalCreditsHeld.toLocaleString()],
      ["Signup credits granted", data.signupCreditsGranted.toLocaleString()],
      ["Admin adjustments (net)", data.adminAdjustmentsNet.toLocaleString()],
      ["Admin adjustments granted", data.adminAdjustmentsGranted.toLocaleString()],
      ["Admin adjustments deducted", data.adminAdjustmentsDeducted.toLocaleString()],
      ["Gross credits consumed", data.grossCreditsConsumed.toLocaleString()],
      ["Credits refunded", data.creditsRefunded.toLocaleString()],
      ["Net credits consumed", data.netCreditsConsumed.toLocaleString()],
      ["Standard-build usage", data.standardBuildUsage.toLocaleString()],
      ["Validated-build usage", data.validatedBuildUsage.toLocaleString()],
      ["Purchased credits", data.purchasedCredits.toLocaleString()],
    ]);
  } catch {
    container.innerHTML = "<p>Unavailable. Please refresh.</p>";
  }
}

// --- Users ------------------------------------------------------------

async function loadUsers(emailFilter) {
  const wrap = el("usersTableWrap");
  wrap.innerHTML = "<p role=\"status\">Loading&hellip;</p>";
  try {
    const url = emailFilter ? `/api/admin/users?email=${encodeURIComponent(emailFilter)}` : "/api/admin/users";
    const response = await apiFetch(url);
    if (!response.ok) throw new Error("Users unavailable");
    const users = await response.json();
    if (users.length === 0) {
      wrap.innerHTML = "<p>No users found.</p>";
      return;
    }
    const rows = users.map((u) => {
      const link = document.createElement("button");
      link.type = "button";
      link.className = "link";
      link.textContent = u.email || u.id;
      link.addEventListener("click", () => showUserDetail(u.id));
      return [link, u.lockedOut ? "Locked" : "Active", u.creditBalance, u.pluginCount, u.versionCount];
    });
    wrap.innerHTML = "";
    wrap.appendChild(table(["Email", "Status", "Credits", "Plugins", "Versions"], rows));
  } catch {
    wrap.innerHTML = "<p>Unavailable. Please refresh.</p>";
  }
}

async function showUserDetail(id) {
  const panel = el("userDetailPanel");
  const body = el("userDetailBody");
  panel.hidden = false;
  body.innerHTML = "<p role=\"status\">Loading&hellip;</p>";
  panel.scrollIntoView({ behavior: "smooth", block: "start" });
  try {
    const response = await apiFetch(`/api/admin/users/${encodeURIComponent(id)}`);
    if (!response.ok) throw new Error("User detail unavailable");
    const u = await response.json();

    body.innerHTML = "";
    const heading = document.createElement("h2");
    heading.className = "panel-title";
    heading.textContent = u.email || u.id;
    body.appendChild(heading);

    const grid = document.createElement("div");
    grid.className = "admin-detail-grid";

    const account = document.createElement("div");
    account.innerHTML = `<h3>Account</h3><p>${u.lockedOut ? "Locked" : "Active"}${u.isAdmin ? " · Admin" : ""}</p>`;
    if (!u.isAdmin) {
      const lockButton = document.createElement("button");
      lockButton.type = "button";
      lockButton.className = "button-secondary button-small";
      lockButton.textContent = u.lockedOut ? "Unlock account" : "Lock account";
      lockButton.addEventListener("click", () => toggleLock(u.id, !u.lockedOut));
      account.appendChild(lockButton);
    }
    grid.appendChild(account);

    const credits = document.createElement("div");
    credits.innerHTML = `<h3>Credits</h3><p>${u.creditBalance.toLocaleString()} balance · `
      + `${u.creditsConsumed.toLocaleString()} consumed</p>`;
    grid.appendChild(credits);

    const plugins = document.createElement("div");
    plugins.innerHTML = `<h3>Plugins / Builds</h3><p>${u.pluginCount.toLocaleString()} plugins · ${u.versionCount.toLocaleString()} versions</p>`;
    grid.appendChild(plugins);

    const ai = document.createElement("div");
    ai.innerHTML = `<h3>AI Usage</h3><p>${u.aiRequestCount.toLocaleString()} requests · ${formatTokens(u.aiTotalTokens)} tokens · ${formatUsd(u.aiEstimatedCostUsdMicros)}</p>`;
    grid.appendChild(ai);

    const payments = document.createElement("div");
    payments.innerHTML = `<h3>Payments</h3><p>${u.lifetimePurchases.toLocaleString()} purchases · `
      + `${formatGbp(u.lifetimeRevenueMinor)} lifetime revenue`
      + (u.lifetimeRefundedMinor > 0 ? ` · ${formatGbp(u.lifetimeRefundedMinor)} refunded` : "")
      + ` · ${u.creditsPurchased.toLocaleString()} credits purchased</p>`;
    grid.appendChild(payments);

    body.appendChild(grid);

    const activityHeading = document.createElement("h3");
    activityHeading.className = "step-label-spaced";
    activityHeading.textContent = "Recent AI activity";
    body.appendChild(activityHeading);

    if (u.recentAiActivity.length === 0) {
      const p = document.createElement("p");
      p.textContent = "No AI requests yet.";
      body.appendChild(p);
    } else {
      const rows = u.recentAiActivity.map((e) => [
        formatDateTime(e.createdAtUtc),
        e.provider,
        e.model || "-",
        e.succeeded ? "Success" : `Failed (${e.failureCategory || "unknown"})`,
        formatTokens(e.totalTokens),
        formatUsd(e.estimatedCostUsdMicros),
      ]);
      body.appendChild(table(["When", "Provider", "Model", "Result", "Tokens", "Cost"], rows));
    }

    const ledgerHeading = document.createElement("h3");
    ledgerHeading.className = "step-label-spaced";
    ledgerHeading.textContent = "Recent credit activity";
    body.appendChild(ledgerHeading);

    if (u.recentCreditActivity.length === 0) {
      const p = document.createElement("p");
      p.textContent = "No credit activity yet.";
      body.appendChild(p);
    } else {
      const rows = u.recentCreditActivity.map((t) => [
        formatDateTime(t.createdAtUtc),
        t.type,
        t.amount > 0 ? `+${t.amount}` : `${t.amount}`,
      ]);
      body.appendChild(table(["When", "Type", "Amount"], rows));
    }

    const adjustHeading = document.createElement("h3");
    adjustHeading.className = "step-label-spaced";
    adjustHeading.textContent = "Adjust credits";
    body.appendChild(adjustHeading);
    body.appendChild(buildAdjustForm(u.id));
  } catch {
    body.innerHTML = "<p>Unavailable. Please refresh.</p>";
  }
}

function buildAdjustForm(userId) {
  const form = document.createElement("form");
  form.className = "admin-adjust-form";
  form.noValidate = true;

  const amountField = document.createElement("div");
  amountField.className = "field";
  amountField.innerHTML = '<label for="adjustAmount">Adjustment (e.g. +50 or -20)</label>';
  const amountInput = document.createElement("input");
  amountInput.type = "number";
  amountInput.id = "adjustAmount";
  amountInput.required = true;
  amountInput.step = "1";
  amountField.appendChild(amountInput);
  form.appendChild(amountField);

  const reasonField = document.createElement("div");
  reasonField.className = "field";
  reasonField.innerHTML = '<label for="adjustReason">Reason</label>';
  const reasonInput = document.createElement("input");
  reasonInput.type = "text";
  reasonInput.id = "adjustReason";
  reasonInput.required = true;
  reasonInput.placeholder = "Customer support compensation";
  reasonField.appendChild(reasonInput);
  form.appendChild(reasonField);

  const submitButton = document.createElement("button");
  submitButton.type = "submit";
  submitButton.className = "button button-small";
  submitButton.textContent = "Apply adjustment";
  form.appendChild(submitButton);

  const status = document.createElement("p");
  status.className = "loading-line";
  status.hidden = true;
  form.appendChild(status);

  // One idempotency key per form instance - stable across a resubmit caused
  // by a slow network, but a fresh form (new page load) gets a new key.
  const idempotencyKey = crypto.randomUUID();

  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    const amount = parseInt(amountInput.value, 10);
    if (!Number.isFinite(amount) || amount === 0) {
      status.hidden = false;
      status.textContent = "Enter a non-zero whole-number amount.";
      return;
    }
    if (!reasonInput.value.trim()) {
      status.hidden = false;
      status.textContent = "A reason is required.";
      return;
    }

    submitButton.disabled = true;
    status.hidden = false;
    status.textContent = "Applying…";
    try {
      const response = await apiFetch(`/api/admin/users/${encodeURIComponent(userId)}/credits/adjust`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ amount, reason: reasonInput.value.trim(), idempotencyKey }),
      });
      if (response.status === 409) {
        status.textContent = "Adjustment would take the balance below zero.";
        submitButton.disabled = false;
        return;
      }
      if (!response.ok) {
        const errorBody = await response.json().catch(() => null);
        status.textContent = errorBody?.error || "Adjustment failed.";
        submitButton.disabled = false;
        return;
      }
      const result = await response.json();
      status.textContent = `Applied. New balance: ${result.balance.toLocaleString()}.`;
      showUserDetail(userId);
    } catch {
      status.textContent = "Adjustment failed. Please retry.";
      submitButton.disabled = false;
    }
  });

  return form;
}

async function toggleLock(userId, lock) {
  if (!window.confirm(lock ? "Lock this account?" : "Unlock this account?")) return;
  try {
    const response = await apiFetch(`/api/admin/users/${encodeURIComponent(userId)}/${lock ? "lock" : "unlock"}`, { method: "POST" });
    if (!response.ok) throw new Error("Lock/unlock failed");
    showUserDetail(userId);
  } catch {
    window.alert("Could not update the account lock state. Please retry.");
  }
}

// --- Plugins ------------------------------------------------------------

async function loadPlugins() {
  const wrap = el("pluginsTableWrap");
  try {
    const response = await apiFetch("/api/admin/plugins");
    if (!response.ok) throw new Error("Plugins unavailable");
    const projects = await response.json();
    if (projects.length === 0) {
      wrap.innerHTML = "<p>No plugin projects yet.</p>";
      return;
    }
    const rows = projects.map((p) => [
      p.userEmail || p.userId,
      p.name,
      p.slug,
      p.versionCount,
      p.latestValidated ? "Validated" : "Standard",
      formatDate(p.updatedAtUtc),
    ]);
    wrap.innerHTML = "";
    wrap.appendChild(table(["User", "Name", "Slug", "Versions", "Latest build", "Updated"], rows));
  } catch {
    wrap.innerHTML = "<p>Unavailable. Please refresh.</p>";
  }
}

// --- AI usage -------------------------------------------------------------

async function loadAiUsage() {
  const statsContainer = el("aiUsageStats");
  const providerContainer = el("aiByProvider");
  const modelContainer = el("aiByModel");
  const recentContainer = el("aiRecent");
  try {
    const response = await apiFetch("/api/admin/ai-usage");
    if (!response.ok) throw new Error("AI usage unavailable");
    const data = await response.json();

    renderStats(statsContainer, [
      ["Requests", data.requests.toLocaleString()],
      ["Successful", data.successfulRequests.toLocaleString()],
      ["Failed", data.failedRequests.toLocaleString()],
      ["Input tokens", formatTokens(data.inputTokens)],
      ["Output tokens", formatTokens(data.outputTokens)],
      ["Total tokens", formatTokens(data.totalTokens)],
      ["Estimated cost", formatUsd(data.estimatedCostUsdMicros)],
      ["Avg cost / request", data.averageCostUsdMicrosPerRequest ? formatUsd(data.averageCostUsdMicrosPerRequest) : "Unknown"],
    ]);

    providerContainer.innerHTML = "";
    providerContainer.appendChild(table(
      ["Provider", "Requests", "Tokens", "Estimated cost"],
      data.byProvider.map((r) => [r.key, r.requests, formatTokens(r.totalTokens), formatUsd(r.estimatedCostUsdMicros)]),
    ));

    modelContainer.innerHTML = "";
    modelContainer.appendChild(table(
      ["Model", "Requests", "Tokens", "Estimated cost"],
      data.byModel.map((r) => [r.key, r.requests, formatTokens(r.totalTokens), formatUsd(r.estimatedCostUsdMicros)]),
    ));

    recentContainer.innerHTML = "";
    if (data.recentEvents.length === 0) {
      recentContainer.innerHTML = "<p>No AI requests yet.</p>";
    } else {
      recentContainer.appendChild(table(
        ["When", "User", "Provider", "Model", "Result", "Tokens", "Cost"],
        data.recentEvents.map((e) => [
          formatDateTime(e.createdAtUtc),
          e.userEmail || e.userId || "-",
          e.provider,
          e.model || "-",
          e.succeeded ? "Success" : `Failed (${e.failureCategory || "unknown"})`,
          formatTokens(e.totalTokens),
          formatUsd(e.estimatedCostUsdMicros),
        ]),
      ));
    }
  } catch {
    statsContainer.innerHTML = "<p>Unavailable. Please refresh.</p>";
  }
}

// --- Audit log ------------------------------------------------------------

async function loadAudit(targetId) {
  const wrap = el("auditTableWrap");
  wrap.innerHTML = "<p role=\"status\">Loading&hellip;</p>";
  try {
    const url = targetId ? `/api/admin/audit-log?targetId=${encodeURIComponent(targetId)}` : "/api/admin/audit-log";
    const response = await apiFetch(url);
    if (!response.ok) throw new Error("Audit log unavailable");
    const entries = await response.json();
    if (entries.length === 0) {
      wrap.innerHTML = "<p>No audit entries yet.</p>";
      return;
    }
    const rows = entries.map((e) => [
      formatDateTime(e.createdAtUtc),
      e.adminEmail || e.adminUserId,
      e.action,
      e.targetEmail || e.targetId,
      e.description || "-",
    ]);
    wrap.innerHTML = "";
    wrap.appendChild(table(["When", "Administrator", "Action", "Target", "Details"], rows));
  } catch {
    wrap.innerHTML = "<p>Unavailable. Please refresh.</p>";
  }
}

// --- Revenue ------------------------------------------------------------

function dateRangeParams(range) {
  const now = new Date();
  let from = null;
  if (range === "today") {
    from = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  } else if (range === "7days") {
    from = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000);
  } else if (range === "30days") {
    from = new Date(now.getTime() - 30 * 24 * 60 * 60 * 1000);
  } else if (range === "month") {
    from = new Date(now.getFullYear(), now.getMonth(), 1);
  }
  return from ? `?from=${encodeURIComponent(from.toISOString())}` : "";
}

let currentRevenueRange = "all";

async function loadRevenue() {
  const statsContainer = el("revenueStats");
  const byPeriodContainer = el("revenueByPeriod");
  const recentContainer = el("recentPurchases");
  const contributionContainer = el("contributionStats");
  const operationalContainer = el("operationalStats");

  const query = dateRangeParams(currentRevenueRange);
  try {
    const response = await apiFetch(`/api/admin/finance/revenue${query}`);
    if (!response.ok) throw new Error("Revenue unavailable");
    const data = await response.json();

    renderStats(statsContainer, [
      ["Gross revenue", formatGbp(data.grossRevenueMinor)],
      ["Refunded", formatGbp(data.refundedMinor)],
      ["Net revenue", formatGbp(data.netRevenueMinor)],
      ["Successful purchases", data.successfulPurchases.toLocaleString()],
      ["Failed / cancelled", data.failedOrCancelledPurchases.toLocaleString()],
      ["Credits sold", data.creditsSold.toLocaleString()],
      ["Purchasing customers", data.purchasingCustomers.toLocaleString()],
      ["Average purchase", data.averagePurchaseMinor ? formatGbp(data.averagePurchaseMinor) : "Unknown"],
    ]);

    byPeriodContainer.innerHTML = "";
    if (data.byPeriod.length === 0) {
      byPeriodContainer.innerHTML = "<p>No purchases in this range.</p>";
    } else {
      byPeriodContainer.appendChild(table(
        ["Date", "Revenue", "Purchases", "Credits sold"],
        data.byPeriod.map((r) => [r.date, formatGbp(r.grossRevenueMinor), r.successfulPurchases, r.creditsSold]),
      ));
    }

    recentContainer.innerHTML = "";
    if (data.recentPurchases.length === 0) {
      recentContainer.innerHTML = "<p>No purchases yet.</p>";
    } else {
      recentContainer.appendChild(table(
        ["When", "Customer", "Pack", "Amount", "Credits", "Status"],
        data.recentPurchases.map((p) => [
          formatDateTime(p.createdAtUtc), p.userEmail || p.userId, p.packId,
          formatGbp(p.amountMinor), p.creditsPurchased, p.status,
        ]),
      ));
    }
  } catch {
    statsContainer.innerHTML = "<p>Unavailable. Please refresh.</p>";
  }

  try {
    const response = await apiFetch("/api/admin/finance/contribution");
    if (!response.ok) throw new Error("Contribution unavailable");
    const data = await response.json();
    renderStats(contributionContainer, [
      ["Revenue", formatGbp(data.revenueMinor)],
      ["Refunded revenue", formatGbp(data.refundedRevenueMinor)],
      ["Estimated AI spend", formatUsd(data.estimatedAiSpendUsdMicros)],
      ["Revenue / customer", data.revenuePerCustomerMinor ? formatGbp(data.revenuePerCustomerMinor) : "Unknown"],
      ["AI cost / customer", data.aiCostPerCustomerUsdMicros ? formatUsd(data.aiCostPerCustomerUsdMicros) : "Unknown"],
      ["Revenue / build", data.revenuePerBuildMinor ? formatGbp(data.revenuePerBuildMinor) : "Unknown"],
      ["AI cost / build", data.aiCostPerBuildUsdMicros ? formatUsd(data.aiCostPerBuildUsdMicros) : "Unknown"],
    ]);
  } catch {
    contributionContainer.innerHTML = "<p>Unavailable. Please refresh.</p>";
  }

  try {
    const response = await apiFetch(`/api/admin/finance/operational${query}`);
    if (!response.ok) throw new Error("Operational analytics unavailable");
    const data = await response.json();
    renderStats(operationalContainer, [
      ["Plan generations", data.planGenerations.toLocaleString()],
      ["Plan generation failures", data.planGenerationFailures.toLocaleString()],
      ["Total builds", data.totalBuilds.toLocaleString()],
      ["Standard builds", data.standardBuilds.toLocaleString()],
      ["Validated builds", data.validatedBuilds.toLocaleString()],
      ["Refunded builds", data.refundedBuilds.toLocaleString()],
      ["Credits consumed", data.creditsConsumed.toLocaleString()],
      ["AI requests", data.aiRequests.toLocaleString()],
      ["AI failures", data.aiFailures.toLocaleString()],
      ["Purchases", data.purchases.toLocaleString()],
      ["Revenue", formatGbp(data.revenueMinor)],
      ["Payment webhook failures", data.paymentWebhookFailures.toLocaleString()],
    ]);
  } catch {
    operationalContainer.innerHTML = "<p>Unavailable. Please refresh.</p>";
  }
}

// --- Promotions -----------------------------------------------------------

const PROMOTION_TYPES = ["FreeBuilds", "BonusCredits", "PackPriceDiscount"];
const PROMOTION_ELIGIBILITY = ["Everyone", "NewRegistrations", "FirstPurchaseOnly"];
let cachedPackIds = null;

async function getPackIds() {
  if (cachedPackIds) return cachedPackIds;
  try {
    const response = await apiFetch("/api/payments/packs");
    if (!response.ok) throw new Error("Packs unavailable");
    cachedPackIds = (await response.json()).map((p) => p.packId);
  } catch {
    cachedPackIds = [];
  }
  return cachedPackIds;
}

function valueLabel(promotion) {
  if (promotion.type === "PackPriceDiscount") return `${promotion.value}% off`;
  if (promotion.type === "BonusCredits") return `+${promotion.value} bonus credits`;
  return `+${promotion.value} free builds`;
}

async function loadPromotions() {
  const wrap = el("promotionsTableWrap");
  wrap.innerHTML = "<p role=\"status\">Loading&hellip;</p>";
  try {
    const response = await apiFetch("/api/admin/promotions");
    if (!response.ok) throw new Error("Promotions unavailable");
    const promotions = await response.json();
    if (promotions.length === 0) {
      wrap.innerHTML = "<p>No promotions yet.</p>";
      return;
    }
    const rows = promotions.map((p) => {
      const nameLink = document.createElement("button");
      nameLink.type = "button";
      nameLink.className = "link";
      nameLink.textContent = p.name;
      nameLink.addEventListener("click", () => showPromotionDetail(p.id));

      const editBtn = document.createElement("button");
      editBtn.type = "button";
      editBtn.className = "button-secondary button-small";
      editBtn.textContent = "Edit";
      editBtn.addEventListener("click", () => openPromotionForm(p));

      const toggleBtn = document.createElement("button");
      toggleBtn.type = "button";
      toggleBtn.className = "button-secondary button-small";
      toggleBtn.textContent = p.isEnabled ? "Disable" : "Enable";
      toggleBtn.addEventListener("click", () => togglePromotion(p.id, !p.isEnabled));

      const dupBtn = document.createElement("button");
      dupBtn.type = "button";
      dupBtn.className = "button-secondary button-small";
      dupBtn.textContent = "Duplicate";
      dupBtn.addEventListener("click", () => duplicatePromotion(p.id));

      const actions = document.createElement("div");
      actions.className = "admin-row-actions";
      actions.append(editBtn, toggleBtn, dupBtn);

      return [nameLink, p.type, p.code || "-", valueLabel(p), formatDate(p.startsAtUtc),
        p.endsAtUtc ? formatDate(p.endsAtUtc) : "-", p.state, p.redemptionCount, actions];
    });
    wrap.innerHTML = "";
    wrap.appendChild(table(
      ["Name", "Type", "Code", "Benefit", "Start", "End", "State", "Redemptions", "Actions"], rows));
  } catch {
    wrap.innerHTML = "<p>Unavailable. Please refresh.</p>";
  }
}

async function showPromotionDetail(id) {
  const panel = el("promotionDetailPanel");
  const body = el("promotionDetailBody");
  panel.hidden = false;
  el("promotionFormPanel").hidden = true;
  body.innerHTML = "<p role=\"status\">Loading&hellip;</p>";
  panel.scrollIntoView({ behavior: "smooth", block: "start" });
  try {
    const response = await apiFetch(`/api/admin/promotions/${encodeURIComponent(id)}`);
    if (!response.ok) throw new Error("Promotion detail unavailable");
    const p = await response.json();
    body.innerHTML = "";
    const heading = document.createElement("h2");
    heading.className = "panel-title";
    heading.textContent = `${p.name} (${p.state})`;
    body.appendChild(heading);
    body.appendChild(table(["Metric", "Value"], [
      ["Type / benefit", valueLabel(p)],
      ["Code", p.code || "Automatic (no code)"],
      ["Redemptions", p.redemptionCount],
      ["Purchasing customers", p.purchasingCustomers],
      ["Credits granted", p.creditsGranted.toLocaleString()],
      ["Free builds granted", p.freeBuildsGranted.toLocaleString()],
      ["Discount granted", formatGbp(p.discountMinorGranted)],
      ["Revenue associated", formatGbp(p.revenueMinor)],
    ]));
  } catch {
    body.innerHTML = "<p>Unavailable. Please refresh.</p>";
  }
}

function toIsoOrNull(value) {
  return value ? new Date(`${value}:00.000Z`).toISOString() : null;
}

function toDateTimeLocal(iso) {
  return iso ? iso.slice(0, 16) : "";
}

async function openPromotionForm(promotion) {
  el("promotionDetailPanel").hidden = true;
  el("promotionFormPanel").hidden = false;
  el("promotionFormTitle").textContent = promotion ? `Edit ${promotion.name}` : "New promotion";
  el("promotionFormWrap").innerHTML = "";
  el("promotionFormWrap").appendChild(await buildPromotionForm(promotion));
  el("promotionFormPanel").scrollIntoView({ behavior: "smooth", block: "start" });
}

async function buildPromotionForm(promotion) {
  const packIds = await getPackIds();
  const form = document.createElement("form");
  form.className = "admin-promotion-form";
  form.noValidate = true;

  const field = (labelText, inputEl) => {
    const wrap = document.createElement("div");
    wrap.className = "field";
    const label = document.createElement("label");
    label.textContent = labelText;
    if (inputEl.id) label.htmlFor = inputEl.id;
    wrap.append(label, inputEl);
    return wrap;
  };

  const nameInput = document.createElement("input");
  nameInput.type = "text"; nameInput.required = true; nameInput.value = promotion?.name || "";
  form.appendChild(field("Name", nameInput));

  const typeSelect = document.createElement("select");
  PROMOTION_TYPES.forEach((t) => {
    const opt = document.createElement("option");
    opt.value = t; opt.textContent = t;
    if (promotion?.type === t) opt.selected = true;
    typeSelect.appendChild(opt);
  });
  form.appendChild(field("Type", typeSelect));

  const valueInput = document.createElement("input");
  valueInput.type = "number"; valueInput.min = "1"; valueInput.required = true;
  valueInput.value = promotion?.value ?? "";
  form.appendChild(field("Value (% for PackPriceDiscount, count otherwise)", valueInput));

  const codeInput = document.createElement("input");
  codeInput.type = "text"; codeInput.value = promotion?.code || "";
  codeInput.placeholder = "WELCOME25";
  form.appendChild(field("Code (blank = automatic, no code)", codeInput));

  const requiresCodeInput = document.createElement("input");
  requiresCodeInput.type = "checkbox"; requiresCodeInput.checked = promotion?.requiresCode ?? true;
  form.appendChild(field("Requires code", requiresCodeInput));

  const packSelect = document.createElement("select");
  const allOpt = document.createElement("option");
  allOpt.value = ""; allOpt.textContent = "All packs";
  packSelect.appendChild(allOpt);
  packIds.forEach((id) => {
    const opt = document.createElement("option");
    opt.value = id; opt.textContent = id;
    if (promotion?.appliesToPackId === id) opt.selected = true;
    packSelect.appendChild(opt);
  });
  form.appendChild(field("Applies to pack", packSelect));

  const eligibilitySelect = document.createElement("select");
  PROMOTION_ELIGIBILITY.forEach((e) => {
    const opt = document.createElement("option");
    opt.value = e; opt.textContent = e;
    if (promotion?.eligibility === e) opt.selected = true;
    eligibilitySelect.appendChild(opt);
  });
  form.appendChild(field("Eligibility", eligibilitySelect));

  const startsInput = document.createElement("input");
  startsInput.type = "datetime-local"; startsInput.required = true;
  startsInput.value = toDateTimeLocal(promotion?.startsAtUtc) || toDateTimeLocal(new Date().toISOString());
  form.appendChild(field("Starts (UTC)", startsInput));

  const endsInput = document.createElement("input");
  endsInput.type = "datetime-local";
  endsInput.value = toDateTimeLocal(promotion?.endsAtUtc);
  form.appendChild(field("Ends (UTC, blank = no end)", endsInput));

  const priorityInput = document.createElement("input");
  priorityInput.type = "number"; priorityInput.value = promotion?.priority ?? 0;
  form.appendChild(field("Priority (higher wins among automatic deals)", priorityInput));

  const maxRedemptionsInput = document.createElement("input");
  maxRedemptionsInput.type = "number"; maxRedemptionsInput.min = "1";
  maxRedemptionsInput.value = promotion?.maxRedemptions ?? "";
  form.appendChild(field("Max total redemptions (blank = unlimited)", maxRedemptionsInput));

  const maxPerUserInput = document.createElement("input");
  maxPerUserInput.type = "number"; maxPerUserInput.min = "1";
  maxPerUserInput.value = promotion?.maxRedemptionsPerUser ?? "";
  form.appendChild(field("Max redemptions per user (blank = unlimited)", maxPerUserInput));

  const enabledInput = document.createElement("input");
  enabledInput.type = "checkbox"; enabledInput.checked = promotion?.isEnabled ?? false;
  form.appendChild(field("Enabled", enabledInput));

  const submitButton = document.createElement("button");
  submitButton.type = "submit";
  submitButton.className = "button button-small";
  submitButton.textContent = promotion ? "Save changes" : "Create promotion";
  form.appendChild(submitButton);

  const cancelButton = document.createElement("button");
  cancelButton.type = "button";
  cancelButton.className = "button-secondary button-small";
  cancelButton.textContent = "Cancel";
  cancelButton.addEventListener("click", () => { el("promotionFormPanel").hidden = true; });
  form.appendChild(cancelButton);

  const status = document.createElement("p");
  status.className = "loading-line";
  status.hidden = true;
  form.appendChild(status);

  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    submitButton.disabled = true;
    status.hidden = false;
    status.textContent = "Saving…";

    const body = {
      name: nameInput.value.trim(),
      type: typeSelect.value,
      value: parseInt(valueInput.value, 10),
      code: codeInput.value.trim() || null,
      requiresCode: requiresCodeInput.checked,
      appliesToPackId: packSelect.value || null,
      eligibility: eligibilitySelect.value,
      startsAtUtc: toIsoOrNull(startsInput.value),
      endsAtUtc: toIsoOrNull(endsInput.value),
      priority: parseInt(priorityInput.value, 10) || 0,
      maxRedemptions: maxRedemptionsInput.value ? parseInt(maxRedemptionsInput.value, 10) : null,
      maxRedemptionsPerUser: maxPerUserInput.value ? parseInt(maxPerUserInput.value, 10) : null,
      isEnabled: enabledInput.checked,
    };

    try {
      const url = promotion ? `/api/admin/promotions/${encodeURIComponent(promotion.id)}` : "/api/admin/promotions";
      const response = await apiFetch(url, {
        method: promotion ? "PUT" : "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      if (!response.ok) {
        const errorBody = await response.json().catch(() => null);
        status.textContent = errorBody?.error || "Save failed.";
        submitButton.disabled = false;
        return;
      }
      el("promotionFormPanel").hidden = true;
      loaded.delete("promotions");
      loadTab("promotions");
    } catch {
      status.textContent = "Save failed. Please retry.";
      submitButton.disabled = false;
    }
  });

  return form;
}

async function togglePromotion(id, enable) {
  try {
    const response = await apiFetch(`/api/admin/promotions/${encodeURIComponent(id)}/${enable ? "enable" : "disable"}`, { method: "POST" });
    if (!response.ok) throw new Error("Toggle failed");
    loaded.delete("promotions");
    loadTab("promotions");
  } catch {
    window.alert("Could not update the promotion. Please retry.");
  }
}

async function duplicatePromotion(id) {
  try {
    const response = await apiFetch(`/api/admin/promotions/${encodeURIComponent(id)}/duplicate`, { method: "POST" });
    if (!response.ok) throw new Error("Duplicate failed");
    loaded.delete("promotions");
    loadTab("promotions");
  } catch {
    window.alert("Could not duplicate the promotion. Please retry.");
  }
}

// --- System ------------------------------------------------------------

async function loadSystem() {
  const container = el("systemStatus");
  try {
    const response = await apiFetch("/api/admin/system");
    if (!response.ok) throw new Error("System status unavailable");
    const s = await response.json();
    container.innerHTML = "";
    container.appendChild(table(["Check", "Status"], [
      ["Application version", s.applicationVersion || "Unknown"],
      ["Environment", s.environment],
      ["Database", s.databaseHealthy ? "Healthy" : "Unhealthy"],
      ["AI provider", s.planningProvider],
      ["AI provider configured", s.planningProviderConfigured ? "Yes" : "No"],
      ["Docker validation available", s.dockerValidationAvailable ? "Yes" : "No"],
      ["Artifact storage writable", s.artifactStorageWritable ? "Yes" : "No"],
      ["Stripe configured", s.stripeConfigured ? "Yes" : "No"],
      ["Transactional email configured", s.transactionalEmailConfigured ? "Yes" : "No"],
    ]));
  } catch {
    container.innerHTML = "<p>Unavailable. Please refresh.</p>";
  }
}

// --- Init ------------------------------------------------------------

(async () => {
  try {
    const response = await apiFetch("/api/account/me");
    if (!response.ok) {
      window.location.href = "login.html";
      return;
    }
    const me = await response.json();
    if (!me.isAdmin) {
      window.location.href = "dashboard.html";
      return;
    }
    el("admin").hidden = false;
    initTabs();
    loadOverview();
    loaded.add("overview");

    el("userSearch").addEventListener("input", (event) => {
      clearTimeout(window.__userSearchTimer);
      window.__userSearchTimer = setTimeout(() => loadUsers(event.target.value.trim()), 250);
    });
    el("userDetailBack").addEventListener("click", () => {
      el("userDetailPanel").hidden = true;
    });

    el("auditTargetSearch").addEventListener("input", (event) => {
      clearTimeout(window.__auditSearchTimer);
      window.__auditSearchTimer = setTimeout(() => loadAudit(event.target.value.trim()), 250);
    });

    el("newPromotionBtn").addEventListener("click", () => openPromotionForm(null));
    el("promotionDetailBack").addEventListener("click", () => {
      el("promotionDetailPanel").hidden = true;
    });

    document.querySelectorAll(".admin-date-filter").forEach((button) => {
      button.addEventListener("click", () => {
        document.querySelectorAll(".admin-date-filter").forEach((b) => b.removeAttribute("aria-current"));
        button.setAttribute("aria-current", "true");
        currentRevenueRange = button.dataset.range;
        loaded.delete("revenue");
        loadTab("revenue");
      });
    });
  } catch {
    window.location.href = "login.html";
  }
})();
