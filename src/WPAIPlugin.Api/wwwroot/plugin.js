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
      downloadLink.className = "button button-secondary button-small";
      downloadLink.href = v.downloadUrl;
      downloadLink.textContent = "Download ZIP";
      row.appendChild(downloadLink);

      list.appendChild(row);
    });

    el("versionsCard").hidden = false;
  } catch (err) {
    el("notFound").hidden = false;
  }
}

loadProject();
