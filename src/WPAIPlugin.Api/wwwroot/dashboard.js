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
      document.getElementById("projectCount").textContent =
        `${projects.length} saved plugin${projects.length === 1 ? "" : "s"}`;
    } catch {
      document.getElementById("projectCount").textContent = "Unavailable. Please refresh.";
    }
  } catch (err) {
    window.location.href = "login.html";
  }
})();
