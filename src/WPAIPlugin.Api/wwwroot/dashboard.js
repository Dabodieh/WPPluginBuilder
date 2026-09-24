function formatDate(iso) {
  try {
    return new Date(iso).toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
  } catch (err) {
    return iso;
  }
}

function renderRecentPlugins(projects) {
  const list = document.getElementById("recentPluginList");
  if (projects.length === 0) {
    document.getElementById("recentEmptyState").hidden = false;
    return;
  }

  projects.forEach((p) => {
    const card = document.createElement("section");
    card.className = "card elev-sm project-card";

    const swatch = document.createElement("div");
    swatch.className = "project-swatch";
    swatch.setAttribute("aria-hidden", "true");
    swatch.textContent = (p.name || "?").trim().charAt(0).toUpperCase() || "?";
    card.appendChild(swatch);

    const info = document.createElement("div");
    info.className = "project-info";

    const title = document.createElement("h3");
    title.className = "project-name";
    title.textContent = p.name;
    info.appendChild(title);

    const meta = document.createElement("p");
    meta.className = "project-meta";
    meta.textContent = `v${p.latestPluginVersion} · Updated ${formatDate(p.updatedAtUtc)}`;
    info.appendChild(meta);

    card.appendChild(info);

    const status = document.createElement("span");
    status.className = p.validated ? "tag tag-accent" : "tag tag-neutral";
    status.textContent = p.validated ? "✓ Validated" : "Not validated";
    card.appendChild(status);

    const openLink = document.createElement("a");
    openLink.className = "btn btn-secondary";
    openLink.href = `plugin.html?id=${encodeURIComponent(p.id)}`;
    openLink.textContent = "Open";
    card.appendChild(openLink);

    list.appendChild(card);
  });
}

(async () => {
  try {
    const response = await apiFetch("/api/account/me");
    if (!response.ok) {
      window.location.href = "login.html";
      return;
    }
    document.getElementById("dashboard").hidden = false;

    const me = await response.json().catch(() => null);
    if (me && me.emailConfirmed === false) {
      document.getElementById("verificationNotice").hidden = false;
    }

    document.getElementById("resendVerificationBtn").addEventListener("click", async () => {
      const btn = document.getElementById("resendVerificationBtn");
      const messageNode = document.getElementById("resendVerificationMessage");
      btn.disabled = true;
      try {
        const resendResponse = await apiFetch("/api/account/resend-verification", { method: "POST" });
        const body = await resendResponse.json().catch(() => null);
        messageNode.textContent = (body && body.message) || "If verification is required, a new verification email has been sent.";
      } catch {
        messageNode.textContent = "Could not reach the server. Please try again.";
      } finally {
        messageNode.hidden = false;
        btn.disabled = false;
      }
    });

    try {
      const creditsResponse = await apiFetch("/api/credits");
      if (!creditsResponse.ok) throw new Error("Credits unavailable");
      const credits = await creditsResponse.json();
      document.getElementById("creditBalance").textContent = `${credits.balance} remaining`;
      document.getElementById("freeBuildsBalance").textContent =
        `${credits.freeBuildsRemaining} remaining`;
    } catch {
      document.getElementById("creditBalance").textContent = "Unavailable. Please refresh.";
      document.getElementById("freeBuildsBalance").textContent = "Unavailable. Please refresh.";
    }

    try {
      const projectsResponse = await apiFetch("/api/projects");
      if (!projectsResponse.ok) throw new Error("Projects unavailable");
      const projects = await projectsResponse.json();
      document.getElementById("projectCount").textContent = `${projects.length}`;
      renderRecentPlugins(projects.slice(0, 5));
    } catch {
      document.getElementById("projectCount").textContent = "Unavailable";
    }
  } catch (err) {
    window.location.href = "login.html";
  }
})();
