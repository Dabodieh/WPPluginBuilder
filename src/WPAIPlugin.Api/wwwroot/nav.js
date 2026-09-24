// Shared authenticated app-shell top navigation. Injects markup into
// <div id="appNav" data-active="..."></div>, wires logout, and fills the
// email/credit balance from existing authenticated endpoints only.
(function () {
  function buildNav(active) {
    const link = (href, label, key) => {
      const current = key === active ? ' aria-current="page"' : "";
      return `<a href="${href}"${current}>${label}</a>`;
    };
    return `
      <div class="app-shell nav">
        <a class="nav-brand" href="/">ModuleMint</a>
        <span class="nav-links-group">
          ${link("dashboard.html", "Dashboard", "dashboard")}
          ${link("builder.html", "Builder", "builder")}
          ${link("myplugins.html", "My Plugins", "myplugins")}
          ${link("billing.html", "Billing", "billing")}
          <span id="navAdminLink" hidden>${link("admin.html", "Admin", "admin")}</span>
        </span>
        <span class="tag tag-outline nav-credits" id="navCredits" role="status">&hellip; credits</span>
        <details class="nav-account">
          <summary class="btn btn-ghost">Account <span aria-hidden="true">&#9662;</span></summary>
          <div class="nav-account-menu">
            <p>Signed in as</p>
            <p class="nav-account-email" id="navEmail">&hellip;</p>
            <button type="button" class="btn btn-secondary" id="navLogout">Log out</button>
          </div>
        </details>
      </div>`;
  }

  async function initNav() {
    const mount = document.getElementById("appNav");
    if (!mount) return;
    mount.innerHTML = buildNav(mount.dataset.active || "");

    try {
      const response = await apiFetch("/api/account/me");
      if (!response.ok) {
        window.location.href = "login.html";
        return;
      }
      const body = await response.json();
      document.getElementById("navEmail").textContent = body.email || "";
      if (body.isAdmin) document.getElementById("navAdminLink").hidden = false;
    } catch {
      window.location.href = "login.html";
      return;
    }

    try {
      const creditsResponse = await apiFetch("/api/credits");
      if (!creditsResponse.ok) throw new Error("Credits unavailable");
      const credits = await creditsResponse.json();
      document.getElementById("navCredits").textContent = `${credits.balance} credit${credits.balance === 1 ? "" : "s"}`;
    } catch {
      document.getElementById("navCredits").textContent = "Credits unavailable";
    }

    document.getElementById("navLogout").addEventListener("click", async () => {
      try {
        await apiFetch("/api/account/logout", { method: "POST" });
      } finally {
        window.location.href = "login.html";
      }
    });

    document.dispatchEvent(new CustomEvent("appnav:ready"));
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initNav);
  } else {
    initNav();
  }
})();
