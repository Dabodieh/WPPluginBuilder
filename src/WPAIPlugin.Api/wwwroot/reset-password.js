const params = new URLSearchParams(window.location.search);
const email = params.get("email") || "";
const token = params.get("token") || "";

if (!email || !token) {
  document.getElementById("resetPasswordForm").hidden = true;
  const errorNode = document.getElementById("formError");
  errorNode.textContent = "This reset link is invalid or has expired. Please request a new one.";
  errorNode.hidden = false;
}

document.getElementById("resetPasswordForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const noticeNode = document.getElementById("formNotice");
  const errorNode = document.getElementById("formError");
  noticeNode.hidden = true;
  errorNode.hidden = true;

  const newPassword = document.getElementById("newPassword").value;
  const confirmPassword = document.getElementById("confirmPassword").value;

  if (newPassword !== confirmPassword) {
    errorNode.textContent = "Passwords do not match.";
    errorNode.hidden = false;
    return;
  }

  document.getElementById("submitBtn").disabled = true;
  document.getElementById("submitLoading").hidden = false;

  try {
    const response = await apiFetch("/api/account/reset-password", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, token, newPassword, confirmPassword }),
    });

    if (response.status === 429) {
      errorNode.textContent = "Too many requests. Please try again in a few minutes.";
      errorNode.hidden = false;
      return;
    }

    const body = await response.json().catch(() => null);
    if (!response.ok) {
      const message = body && Array.isArray(body.errors) && body.errors.length > 0
        ? body.errors.join(" ")
        : (body && body.error) || "Could not reset your password.";
      errorNode.textContent = message;
      errorNode.hidden = false;
      return;
    }

    noticeNode.textContent = (body && body.message) || "Your password has been reset. You can now log in.";
    noticeNode.hidden = false;
    document.getElementById("resetPasswordForm").hidden = true;
  } catch (err) {
    errorNode.textContent = "Could not reach the server. Please try again.";
    errorNode.hidden = false;
  } finally {
    document.getElementById("submitBtn").disabled = false;
    document.getElementById("submitLoading").hidden = true;
  }
});
