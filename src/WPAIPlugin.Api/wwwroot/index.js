(async () => {
  try {
    const response = await fetch("/api/account/me", { credentials: "same-origin" });
    if (!response.ok) return;
    document.querySelectorAll("[data-build-link]").forEach(link => {
      link.href = "builder.html";
    });
    document.querySelectorAll("[data-account-link]").forEach(link => {
      link.href = "dashboard.html";
      link.textContent = "Dashboard";
    });
  } catch {
    // Keep public registration links usable if the auth check is unavailable.
  }
})();

// The offer is only shown once its live values load - never a hardcoded
// count or price that could drift from server configuration.
(async () => {
  try {
    const response = await fetch("/api/config/public", { credentials: "same-origin" });
    if (!response.ok) return;
    const config = await response.json();
    const freeBuilds = config.signupFreeBuilds;
    if (!(freeBuilds > 0)) return;

    const freeBuildsText = `${freeBuilds} plugin${freeBuilds === 1 ? "" : "s"}`;
    document.querySelectorAll("[data-free-builds]").forEach(node => {
      node.textContent = freeBuildsText;
    });

    const pack = config.startingPack;
    if (pack) {
      document.querySelectorAll("[data-starting-price]").forEach(node => {
        node.textContent = new Intl.NumberFormat("en-GB", { style: "currency", currency: pack.currency })
          .format(pack.amountMinor / 100);
      });
      document.querySelectorAll("[data-starting-credits]").forEach(node => {
        node.textContent = pack.credits.toLocaleString();
      });
      document.querySelectorAll("[data-starting-pack-line]").forEach(node => {
        node.hidden = false;
      });
    }

    document.querySelectorAll("[data-offer]").forEach(node => {
      node.hidden = false;
    });
  } catch {
    // Offer stays hidden rather than showing unverified numbers.
  }
})();
