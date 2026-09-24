(async () => {
  const params = new URLSearchParams(window.location.search);
  const currentEmail = params.get("currentEmail") || "";
  const newEmail = params.get("newEmail") || "";
  const token = params.get("token") || "";
  const leadNode = document.getElementById("pageLead");
  const noticeNode = document.getElementById("formNotice");
  const errorNode = document.getElementById("formError");

  if (!currentEmail || !newEmail || !token) {
    leadNode.textContent = "This confirmation link is invalid.";
    errorNode.textContent = "This confirmation link is invalid or has expired. Please request a new one from your account page.";
    errorNode.hidden = false;
    return;
  }

  try {
    const response = await apiFetch("/api/account/confirm-email-change", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ currentEmail, newEmail, token }),
    });

    const body = await response.json().catch(() => null);

    if (!response.ok) {
      leadNode.textContent = "Confirmation failed.";
      errorNode.textContent = (body && body.error) || "This confirmation link is invalid or has expired. Please request a new one from your account page.";
      errorNode.hidden = false;
      return;
    }

    leadNode.textContent = "Email updated.";
    noticeNode.textContent = (body && body.message) || "Your email address has been updated.";
    noticeNode.hidden = false;
  } catch (err) {
    leadNode.textContent = "Confirmation failed.";
    errorNode.textContent = "Could not reach the server. Please try again.";
    errorNode.hidden = false;
  }
})();
