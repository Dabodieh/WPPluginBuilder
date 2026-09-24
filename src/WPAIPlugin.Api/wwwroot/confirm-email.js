(async () => {
  const params = new URLSearchParams(window.location.search);
  const email = params.get("email") || "";
  const token = params.get("token") || "";
  const leadNode = document.getElementById("pageLead");
  const noticeNode = document.getElementById("formNotice");
  const errorNode = document.getElementById("formError");
  const resendBtn = document.getElementById("resendBtn");

  resendBtn.addEventListener("click", async () => {
    const messageNode = document.getElementById("resendMessage");
    resendBtn.disabled = true;
    try {
      const response = await apiFetch("/api/account/resend-verification", { method: "POST" });
      if (response.status === 401) {
        messageNode.textContent = "Please log in, then request a new verification email from your dashboard.";
      } else {
        const body = await response.json().catch(() => null);
        messageNode.textContent = (body && body.message) || "If verification is required, a new verification email has been sent.";
      }
    } catch {
      messageNode.textContent = "Could not reach the server. Please try again.";
    } finally {
      messageNode.hidden = false;
      resendBtn.disabled = false;
    }
  });

  if (!email || !token) {
    leadNode.textContent = "This verification link is invalid.";
    errorNode.textContent = "This verification link is invalid or has expired. Please request a new one from your dashboard.";
    errorNode.hidden = false;
    resendBtn.hidden = false;
    return;
  }

  try {
    const response = await apiFetch("/api/account/confirm-email", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, token }),
    });

    const body = await response.json().catch(() => null);

    if (!response.ok) {
      leadNode.textContent = "Verification failed.";
      errorNode.textContent = (body && body.error) || "This verification link is invalid or has expired. Please request a new one from your dashboard.";
      errorNode.hidden = false;
      resendBtn.hidden = false;
      return;
    }

    leadNode.textContent = "Email verified.";
    noticeNode.textContent = (body && body.message) || "Your email has been verified. You can now create WordPress plugins.";
    noticeNode.hidden = false;
    document.getElementById("dashboardLink").hidden = false;
  } catch (err) {
    leadNode.textContent = "Verification failed.";
    errorNode.textContent = "Could not reach the server. Please try again.";
    errorNode.hidden = false;
    resendBtn.hidden = false;
  }
})();
