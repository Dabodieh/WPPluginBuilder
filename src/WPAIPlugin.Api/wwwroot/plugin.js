const el = (id) => document.getElementById(id);

function formatDate(iso) {
  try {
    return new Date(iso).toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
  } catch (err) {
    return iso;
  }
}

function getProjectId() {
  const params = new URLSearchParams(window.location.search);
  return params.get("id");
}

async function loadProject() {
  const id = getProjectId();
  if (!id) {
    el("notFound").hidden = false;
    return;
  }

  try {
    const response = await apiFetch(`/api/projects/${encodeURIComponent(id)}`);
    if (response.status === 401) {
      window.location.href = "login.html";
      return;
    }
    if (!response.ok) {
      el("notFound").hidden = false;
      return;
    }

    const project = await response.json();
    el("pluginName").textContent = project.name;
    el("pluginMeta").textContent =
      `Slug: ${project.slug} · Created ${formatDate(project.createdAtUtc)} · Updated ${formatDate(project.updatedAtUtc)}`;

    const latest = project.versions[0];
    if (latest) {
      const downloadLatest = el("downloadLatestLink");
      downloadLatest.href = latest.downloadUrl;
      downloadLatest.hidden = false;
    }

    const list = el("versionList");
    project.versions.forEach((v) => {
      const row = document.createElement("div");
      row.className = "version-row";

      const info = document.createElement("div");

      const title = document.createElement("p");
      title.className = "version-title";
      title.textContent = `Revision ${v.revisionNumber} · Plugin version ${v.pluginVersion}`;
      info.appendChild(title);

      const meta = document.createElement("p");
      meta.className = "version-meta";
      meta.textContent = `${v.validated ? "Validation passed" : "Not validated"} · ${formatDate(v.createdAtUtc)}`;
      info.appendChild(meta);

      row.appendChild(info);

      const downloadLink = document.createElement("a");
      downloadLink.className = "btn btn-secondary btn-download";
      downloadLink.href = v.downloadUrl;
      downloadLink.textContent = "Download ZIP";
      downloadLink.insertAdjacentHTML("beforeend",
        '<svg width="14" height="14" viewBox="0 0 256 256" fill="currentColor" aria-hidden="true" focusable="false"><path d="M221.66,133.66l-72,72a8,8,0,0,1-11.32-11.32L196.69,136H40a8,8,0,0,1,0-16H196.69L138.34,61.66a8,8,0,0,1,11.32-11.32l72,72A8,8,0,0,1,221.66,133.66Z"/></svg>');
      row.appendChild(downloadLink);

      list.appendChild(row);
    });

    el("versionsCard").hidden = false;
  } catch (err) {
    el("notFound").hidden = false;
  }
}

loadProject();
