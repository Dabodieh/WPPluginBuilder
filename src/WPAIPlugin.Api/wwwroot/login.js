document.getElementById("loginForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const errorNode = document.getElementById("loginError");
  errorNode.hidden = true;

  const email = document.getElementById("email").value;
  const password = document.getElementById("password").value;

  document.getElementById("loginBtn").disabled = true;
  document.getElementById("loginLoading").hidden = false;

  try {
    const response = await apiFetch("/api/account/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password }),
    });

    if (!response.ok) {
      const body = await response.json().catch(() => null);
      errorNode.textContent = (body && body.error) || "Invalid email or password.";
      errorNode.hidden = false;
      return;
    }

    window.location.href = "dashboard.html";
  } catch (err) {
    errorNode.textContent = "Could not reach the server. Please try again.";
    errorNode.hidden = false;
  } finally {
    document.getElementById("loginBtn").disabled = false;
    document.getElementById("loginLoading").hidden = true;
  }
});
