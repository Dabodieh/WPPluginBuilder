// Minimal client for the plan -> review -> build -> download flow.
// Calls only the existing /api/plugins/plan and /api/plugins/build endpoints.
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
  const provider = el("provider").value;

  setHidden("resultCard", true);
  el("planBtn").disabled = true;
  setHidden("planLoading", false);

  try {
    const response = await fetch("/api/plugins/plan", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        description: description,
        provider: provider || null,
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

async function buildPlugin() {
  setHidden("buildError", true);
  setHidden("buildSuccess", true);

  const spec = readEditedSpec();

  el("buildBtn").disabled = true;
  setHidden("buildLoading", false);

  try {
    const response = await fetch("/api/plugins/build", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(spec),
    });

    if (!response.ok) {
      const body = await response.json().catch(() => null);
      showError("buildError", extractErrorMessage(body));
      return;
    }

    const blob = await response.blob();
    const disposition = response.headers.get("Content-Disposition") || "";
    const match = /filename="?([^";]+)"?/.exec(disposition);
    const fileName = match ? match[1] : `${spec.slug || "plugin"}.zip`;

    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);

    const successNode = el("buildSuccess");
    successNode.textContent = `Plugin built successfully. Downloaded ${fileName}.`;
    successNode.hidden = false;
  } catch (err) {
    showError("buildError", "Could not reach the build service. Please try again.");
  } finally {
    el("buildBtn").disabled = false;
    setHidden("buildLoading", true);
  }
}

async function buildAndValidatePlugin() {
  setHidden("buildError", true);
  setHidden("buildSuccess", true);

  const spec = readEditedSpec();

  el("buildValidateBtn").disabled = true;
  setHidden("buildValidateLoading", false);

  try {
    const response = await fetch("/api/plugins/build-validated", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(spec),
    });

    if (!response.ok) {
      const body = await response.json().catch(() => null);
      const message = body && body.error ? body.error : extractErrorMessage(body);
      showError("buildError", message);
      return;
    }

    const blob = await response.blob();
    const disposition = response.headers.get("Content-Disposition") || "";
    const match = /filename="?([^";]+)"?/.exec(disposition);
    const fileName = match ? match[1] : `${spec.slug || "plugin"}.zip`;

    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);

    const successNode = el("buildSuccess");
    successNode.textContent =
      `Plugin built and validated successfully.\n\n` +
      `PHP syntax ✓\nWordPress install ✓\nPlugin activation ✓\n\n` +
      `Downloaded ${fileName}.`;
    successNode.style.whiteSpace = "pre-line";
    successNode.hidden = false;
  } catch (err) {
    showError("buildError", "Could not reach the validation service. Please try again.");
  } finally {
    el("buildValidateBtn").disabled = false;
    setHidden("buildValidateLoading", true);
  }
}

el("planBtn").addEventListener("click", planPlugin);
el("buildBtn").addEventListener("click", buildPlugin);
el("buildValidateBtn").addEventListener("click", buildAndValidatePlugin);
