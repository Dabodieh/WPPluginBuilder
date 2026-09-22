// Minimal client for the plan -> review -> build -> download flow.
// Calls only the existing /api/plugins/plan and /api/projects/build endpoints.
// No frameworks, no build step.

let currentSpec = null;
let currentUnsupported = [];

const el = (id) => document.getElementById(id);

function setHidden(id, hidden) {
  el(id).hidden = hidden;
}

function showError(id, message) {
  const node = el(id);
  node.textContent = message;
  node.hidden = false;
}

function clearErrors() {
  setHidden("planError", true);
  setHidden("buildError", true);
  setHidden("buildSuccess", true);
}

function extractErrorMessage(body) {
  if (body && Array.isArray(body.errors) && body.errors.length > 0) {
    return body.errors.join(" ");
  }
  return "Something went wrong. Please try again.";
}

async function planPlugin() {
  clearErrors();
  const description = el("description").value;

  setHidden("resultCard", true);
  el("planBtn").disabled = true;
  setHidden("planLoading", false);

  try {
    const response = await fetch("/api/plugins/plan", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        description: description,
        provider: null,
      }),
    });

    const body = await response.json().catch(() => null);

    if (!response.ok) {
      showError("planError", extractErrorMessage(body));
      return;
    }

    currentSpec = body.spec;
    currentUnsupported = body.unsupportedRequirements || [];
    renderResult();
  } catch (err) {
    showError("planError", "Could not reach the planning service. Please try again.");
  } finally {
    el("planBtn").disabled = false;
    setHidden("planLoading", true);
  }
}

function renderResult() {
  const spec = currentSpec;

  el("f-name").value = spec.name || "";
  el("f-slug").value = spec.slug || "";
  el("f-description").value = spec.description || "";
  el("f-version").value = spec.version || "";
  el("f-author").value = spec.author || "";

  renderFeatureSummary(spec);

  const unsupportedList = el("unsupportedList");
  unsupportedList.innerHTML = "";
  if (currentUnsupported.length > 0) {
    currentUnsupported.forEach((item) => {
      const li = document.createElement("li");
      li.textContent = item;
      unsupportedList.appendChild(li);
    });
    setHidden("unsupportedNotice", false);
  } else {
    setHidden("unsupportedNotice", true);
  }

  setHidden("buildSuccess", true);
  setHidden("buildError", true);
  setHidden("resultCard", false);
}

function renderFeatureSummary(spec) {
  const dl = el("featureSummary");
  dl.innerHTML = "";

  const features = spec.features || [];
  const addRow = (term, value) => {
    const dt = document.createElement("dt");
    dt.textContent = term;
    const dd = document.createElement("dd");
    dd.textContent = value;
    dl.appendChild(dt);
    dl.appendChild(dd);
  };

  const dt = document.createElement("dt");
  dt.textContent = "Features";
  const dd = document.createElement("dd");
  if (features.length > 0) {
    const list = document.createElement("div");
    list.className = "badge-list";
    features.forEach((f) => {
      const span = document.createElement("span");
      span.className = "badge";
      span.textContent = f;
      list.appendChild(span);
    });
    dd.appendChild(list);
  } else {
    dd.textContent = "None";
  }
  dl.appendChild(dt);
  dl.appendChild(dd);

  if (spec.customPostType) {
    const c = spec.customPostType;
    addRow(
      "Custom post type",
      `${c.singularName} / ${c.pluralName} (${c.slug}) — public: ${c.public}, has archive: ${c.hasArchive}`
    );
  }

  if (spec.settingsPage) {
    const s = spec.settingsPage;
    const fieldSummary = (s.fields || []).map((f) => `${f.label} (${f.type})`).join(", ");
    addRow("Settings page", `${s.pageTitle} — fields: ${fieldSummary || "none"}`);
  }

  if (spec.customFields) {
    const c = spec.customFields;
    const fieldSummary = (c.fields || []).map((f) => `${f.label} (${f.type})`).join(", ");
    addRow("Custom fields", `attached to "${c.postType}" — fields: ${fieldSummary || "none"}`);
  }

  if (spec.scheduledTask) {
    const t = spec.scheduledTask;
    addRow("Scheduled task", `${t.taskName} — ${t.schedule}, hook: ${t.hookName}`);
  }
}

function readEditedSpec() {
  // Start from the last planned spec so unedited structured feature data
  // (custom post type, settings page, etc.) is preserved untouched.
  const spec = Object.assign({}, currentSpec);
  spec.name = el("f-name").value;
  spec.slug = el("f-slug").value;
  spec.description = el("f-description").value;
  spec.version = el("f-version").value;
  spec.author = el("f-author").value;
  return spec;
}

async function submitProjectBuild(validated, loadingId, buttonId) {
  setHidden("buildError", true);
  setHidden("buildSuccess", true);
  const spec = readEditedSpec();
  el(buttonId).disabled = true;
  setHidden(loadingId, false);
  try {
    const response = await fetch("/api/projects/build", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ spec: spec, validated: validated }),
    });
    if (!response.ok) {
      const body = await response.json().catch(() => null);
      const message = response.status === 402
        ? `You need ${body.required} credits to ${validated ? "build and validate" : "build"} this plugin. Current balance: ${body.balance}.`
        : body && body.error ? body.error : extractErrorMessage(body);
      showError("buildError", message);
      return;
    }
    const result = await response.json();
    const credits = await refreshCredits();
    const message = validated
      ? "Plugin built and validated successfully.\n\nPHP syntax ✓\nWordPress installation ✓\nPlugin activation ✓\n\nSaved to My Plugins."
      : "Plugin built successfully.\n\nSaved to My Plugins.";
    el("buildSuccessMessage").textContent = `${message}\n${result.creditsCharged} credit${result.creditsCharged === 1 ? "" : "s"} used.\n${credits ? `${credits.balance} credits remaining.` : "Refresh to check your balance."}`;
    el("buildSuccessMessage").style.whiteSpace = "pre-line";
    el("downloadZipLink").href = result.downloadUrl;
    el("viewPluginLink").href = `plugin.html?id=${encodeURIComponent(result.projectId)}`;
    setHidden("buildSuccess", false);
  } catch (err) {
    showError("buildError", "Could not reach the build service. Please try again.");
  } finally {
    await refreshCredits();
    el(buttonId).disabled = false;
    setHidden(loadingId, true);
  }
}
async function buildPlugin() {
  await submitProjectBuild(false, "buildLoading", "buildBtn");
}
async function buildAndValidatePlugin() {
  await submitProjectBuild(true, "buildValidateLoading", "buildValidateBtn");
}
el("createAnotherBtn").addEventListener("click", () => {
  el("description").value = "";
  currentSpec = null;
  currentUnsupported = [];
  setHidden("buildSuccess", true);
  setHidden("resultCard", true);
  el("description").focus();
});
el("planBtn").addEventListener("click", planPlugin);
el("buildBtn").addEventListener("click", buildPlugin);
el("buildValidateBtn").addEventListener("click", buildAndValidatePlugin);
async function refreshAuthNav() {
  try {
    const response = await fetch("/api/account/me");
    if (response.ok) {
      setHidden("loggedOutNav", true);
      setHidden("loggedInNav", false);
    } else {
      setHidden("loggedOutNav", false);
      setHidden("loggedInNav", true);
    }
  } catch (err) {
    setHidden("loggedOutNav", false);
    setHidden("loggedInNav", true);
  }
}
const logoutLink = el("logoutLink");
if (logoutLink) {
  logoutLink.addEventListener("click", async (e) => {
    e.preventDefault();
    try {
      await fetch("/api/account/logout", { method: "POST" });
    } finally {
      window.location.href = "login.html";
    }
  });
}
refreshAuthNav();
async function refreshCredits() {
  try {
    const response = await fetch("/api/credits");
    if (!response.ok) throw new Error("Credits unavailable");
    const credits = await response.json();
    el("creditBalance").textContent = `Credits: ${credits.balance}`;
    el("buildBtn").textContent = `Build Plugin — ${credits.standardBuildCost} credit${credits.standardBuildCost === 1 ? "" : "s"}`;
    el("buildValidateBtn").textContent = `Build & Validate — ${credits.validatedBuildCost} credits`;
    return credits;
  } catch {
    el("creditBalance").textContent = "Credits unavailable. Please log in or refresh.";
    return null;
  }
}
refreshCredits();
