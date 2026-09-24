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
  if (body && typeof body.error === "string" && body.error) {
    return body.error;
  }
  return "Something went wrong. Please try again.";
}

async function planPlugin() {
  clearErrors();
  const description = el("description").value;

  setHidden("resultCard", true);
  setHidden("stepsPreview", false);
  el("planBtn").disabled = true;
  setHidden("planLoading", false);

  try {
    const response = await apiFetch("/api/plugins/plan", {
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
  setHidden("stepsPreview", true);
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

async function submitProjectBuild(validated, loadingId) {
  setHidden("buildError", true);
  setHidden("buildSuccess", true);
  const spec = readEditedSpec();
  el("buildBtn").disabled = true;
  el("buildValidateBtn").disabled = true;
  setHidden(loadingId, false);
  try {
    const response = await apiFetch("/api/projects/build", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ spec: spec, validated: validated }),
    });
    if (!response.ok) {
      const body = await response.json().catch(() => null);
      const message = response.status === 402
        ? `You need ${body.required} credit${body.required === 1 ? "" : "s"} to ${validated ? "build and validate" : "build"} this plugin. Current balance: ${body.balance}.`
        : body && body.error ? body.error : extractErrorMessage(body);
      showError("buildError", message);
      return;
    }
    const result = await response.json();
    const credits = await refreshCredits();
    const message = validated
      ? "Plugin built and validated successfully.\n\nPHP syntax ✓\nWordPress installation ✓\nPlugin activation ✓\n\nSaved to My Plugins."
      : "Plugin built successfully.\n\nSaved to My Plugins.";
    // Only reports a resource as used/remaining when it actually was -
    // never claims a free build was used when a credit covered the build,
    // or vice versa.
    const usageLines = [];
    if (result.freeBuildUsed) usageLines.push("1 free build used.");
    if (result.creditsCharged > 0) usageLines.push(`${result.creditsCharged} credit${result.creditsCharged === 1 ? "" : "s"} used.`);
    usageLines.push(`${result.freeBuildsRemaining} free build${result.freeBuildsRemaining === 1 ? "" : "s"} remaining.`);
    usageLines.push(credits ? `${credits.balance} credit${credits.balance === 1 ? "" : "s"} remaining.` : "Refresh to check your balance.");
    el("buildSuccessMessage").textContent = `${message}\n${usageLines.join("\n")}`;
    el("downloadZipLink").href = result.downloadUrl;
    el("viewPluginLink").href = `plugin.html?id=${encodeURIComponent(result.projectId)}`;
    setHidden("buildSuccess", false);
  } catch (err) {
    showError("buildError", "Could not reach the build service. Please try again.");
  } finally {
    await refreshCredits();
    el("buildBtn").disabled = false;
    el("buildValidateBtn").disabled = false;
    setHidden(loadingId, true);
  }
}
async function buildPlugin() {
  await submitProjectBuild(false, "buildLoading");
}
async function buildAndValidatePlugin() {
  await submitProjectBuild(true, "buildValidateLoading");
}
el("createAnotherBtn").addEventListener("click", () => {
  el("description").value = "";
  currentSpec = null;
  currentUnsupported = [];
  setHidden("buildSuccess", true);
  setHidden("resultCard", true);
  setHidden("stepsPreview", false);
  el("description").focus();
});
el("planBtn").addEventListener("click", planPlugin);
el("buildBtn").addEventListener("click", buildPlugin);
el("buildValidateBtn").addEventListener("click", buildAndValidatePlugin);
async function refreshCredits() {
  try {
    const response = await apiFetch("/api/credits");
    if (!response.ok) throw new Error("Credits unavailable");
    const credits = await response.json();
    el("creditBalance").textContent = `Credits: ${credits.balance}`;
    el("freeBuildsBalance").textContent = `Free builds: ${credits.freeBuildsRemaining}`;
    setHidden("buildValidateOption", credits.validationEnabled === false);
    // Planning never consumes a free build - only Build does.
    el("planFreeBuildsTag").textContent =
      `Planning is free · ${credits.freeBuildsRemaining} free build${credits.freeBuildsRemaining === 1 ? "" : "s"} remaining`;
    setHidden("planFreeBuildsTag", false);
    // Never optimistically decrement either value client-side - this always
    // reflects the server's authoritative count, fetched fresh here.
    if (credits.freeBuildsRemaining > 0) {
      el("buildBtn").textContent = "Build Plugin — Free build";
      el("buildValidateBtn").textContent = "Build & Validate — Free build + 1 credit";
    } else {
      el("buildBtn").textContent = `Build Plugin — ${credits.standardBuildCost} credit${credits.standardBuildCost === 1 ? "" : "s"}`;
      el("buildValidateBtn").textContent = `Build & Validate — ${credits.validatedBuildCost} credit${credits.validatedBuildCost === 1 ? "" : "s"}`;
    }
    return credits;
  } catch {
    el("creditBalance").textContent = "Credits unavailable. Please log in or refresh.";
    el("freeBuildsBalance").textContent = "";
    setHidden("planFreeBuildsTag", true);
    return null;
  }
}
refreshCredits();
