(async () => {
  const params = new URLSearchParams(window.location.search);
  const email = params.get("email") || "";
  const token = params.get("token") || "";
  const leadNode = document.getElementById("pageLead");
  const noticeNode = document.getElementById("formNotice");
  const errorNode = document.getElementById("formError");

  if (!email || !token) {
    leadNode.textContent = "This verification link is invalid.";
    errorNode.textContent = "This verification link is invalid or has expired. Please request a new one from your dashboard.";
    errorNode.hidden = false;
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
      return;
    }

    leadNode.textContent = "Email verified.";
    noticeNode.textContent = (body && body.message) || "Your email has been verified. You can now create WordPress plugins.";
    noticeNode.hidden = false;
  } catch (err) {
    leadNode.textContent = "Verification failed.";
    errorNode.textContent = "Could not reach the server. Please try again.";
    errorNode.hidden = false;
  }
})();
