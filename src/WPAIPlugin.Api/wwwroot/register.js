document.getElementById("registerForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const errorNode = document.getElementById("registerError");
  errorNode.hidden = true;

  const email = document.getElementById("email").value;
  const password = document.getElementById("password").value;
  const confirmPassword = document.getElementById("confirmPassword").value;

  if (password !== confirmPassword) {
    errorNode.textContent = "Passwords do not match.";
    errorNode.hidden = false;
    return;
  }

  document.getElementById("registerBtn").disabled = true;
  document.getElementById("registerLoading").hidden = false;

  try {
    const response = await apiFetch("/api/account/register", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password }),
    });

    if (!response.ok) {
      const body = await response.json().catch(() => null);
      const message = body && Array.isArray(body.errors) && body.errors.length > 0
        ? body.errors.join(" ")
        : (body && body.error) || "Could not create account.";
      errorNode.textContent = message;
      errorNode.hidden = false;
      return;
    }

    window.location.href = "dashboard.html";
  } catch (err) {
    errorNode.textContent = "Could not reach the server. Please try again.";
    errorNode.hidden = false;
  } finally {
    document.getElementById("registerBtn").disabled = false;
    document.getElementById("registerLoading").hidden = true;
  }
});
