const el = (id) => document.getElementById(id);

function formatDate(iso) {
  try {
    return new Date(iso).toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
  } catch (err) {
    return iso;
  }
}

async function loadProjects() {
  try {
    const response = await apiFetch("/api/projects");
    if (!response.ok) {
      window.location.href = "login.html";
      return;
    }

    const projects = await response.json();
    if (projects.length === 0) {
      el("emptyState").hidden = false;
      return;
    }

    const list = el("projectList");
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

      const title = document.createElement("h2");
      title.className = "project-name";
      title.textContent = p.name;
      info.appendChild(title);

      const meta = document.createElement("p");
      meta.className = "project-meta";
      meta.textContent =
        `${p.slug} · v${p.latestPluginVersion} · Revision ${p.latestRevisionNumber} · Updated ${formatDate(p.updatedAtUtc)}`;
      info.appendChild(meta);

      card.appendChild(info);

      const status = document.createElement("span");
      status.className = p.validated ? "tag tag-accent" : "tag tag-neutral";
      status.textContent = p.validated ? "✓ Validated" : "Not validated";
      card.appendChild(status);

      const actions = document.createElement("div");
      actions.className = "project-actions";

      const openLink = document.createElement("a");
      openLink.className = "btn btn-secondary";
      openLink.href = `plugin.html?id=${encodeURIComponent(p.id)}`;
      openLink.textContent = "View";
      actions.appendChild(openLink);

      const downloadLink = document.createElement("a");
      downloadLink.className = "btn btn-secondary btn-download";
      downloadLink.href = p.downloadUrl;
      downloadLink.textContent = "Download";
      downloadLink.setAttribute("aria-label", `Download latest version of ${p.name}`);
      downloadLink.insertAdjacentHTML("beforeend",
        '<svg width="14" height="14" viewBox="0 0 256 256" fill="currentColor" aria-hidden="true" focusable="false"><path d="M221.66,133.66l-72,72a8,8,0,0,1-11.32-11.32L196.69,136H40a8,8,0,0,1,0-16H196.69L138.34,61.66a8,8,0,0,1,11.32-11.32l72,72A8,8,0,0,1,221.66,133.66Z"/></svg>');
      actions.appendChild(downloadLink);

      card.appendChild(actions);
      list.appendChild(card);
    });
  } catch (err) {
    window.location.href = "login.html";
  }
}

loadProjects();
