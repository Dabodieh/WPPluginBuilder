document.getElementById("forgotPasswordForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const noticeNode = document.getElementById("formNotice");
  const errorNode = document.getElementById("formError");
  noticeNode.hidden = true;
  errorNode.hidden = true;

  const email = document.getElementById("email").value;

  document.getElementById("submitBtn").disabled = true;
  document.getElementById("submitLoading").hidden = false;

  try {
    const response = await apiFetch("/api/account/forgot-password", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email }),
    });

    if (response.status === 429) {
      errorNode.textContent = "Too many requests. Please try again in a few minutes.";
      errorNode.hidden = false;
      return;
    }

    // Same generic message whether or not the account exists - the server
    // never reveals account existence through this response.
    const body = await response.json().catch(() => null);
    noticeNode.textContent = (body && body.message)
      || "If an account exists for that email address, a password reset link has been sent.";
    noticeNode.hidden = false;
    document.getElementById("forgotPasswordForm").reset();
  } catch (err) {
    errorNode.textContent = "Could not reach the server. Please try again.";
    errorNode.hidden = false;
  } finally {
    document.getElementById("submitBtn").disabled = false;
    document.getElementById("submitLoading").hidden = true;
  }
});
