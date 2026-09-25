let turnstileEnabled = false;
let turnstileToken = "";
let turnstileWidgetId = null;

async function initTurnstile() {
  try {
    const response = await apiFetch("/api/config/public");
    if (!response.ok) return;
    const config = await response.json();
    turnstileEnabled = Boolean(config.turnstileEnabled) && Boolean(config.turnstileSiteKey);
    if (!turnstileEnabled) return;

    document.getElementById("registerBtn").disabled = true;

    const script = document.createElement("script");
    script.src = "https://challenges.cloudflare.com/turnstile/v0/api.js";
    script.async = true;
    script.defer = true;
    script.onload = () => {
      turnstileWidgetId = window.turnstile.render("#turnstileContainer", {
        sitekey: config.turnstileSiteKey,
        callback: (token) => {
          turnstileToken = token;
          document.getElementById("registerBtn").disabled = false;
        },
        "expired-callback": () => {
          turnstileToken = "";
          document.getElementById("registerBtn").disabled = true;
        },
        "error-callback": () => {
          turnstileToken = "";
          document.getElementById("registerBtn").disabled = true;
        },
      });
    };
    document.head.appendChild(script);
  } catch {
    // Config unavailable - leave turnstileEnabled false. The server enforces
    // its own configuration independently; this only affects whether the
    // browser renders a widget it doesn't need.
  }
}

initTurnstile();

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

  if (turnstileEnabled && !turnstileToken) {
    errorNode.textContent = "Please complete the verification challenge.";
    errorNode.hidden = false;
    return;
  }

  document.getElementById("registerBtn").disabled = true;
  document.getElementById("registerLoading").hidden = false;

  try {
    const response = await apiFetch("/api/account/register", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password, turnstileToken }),
    });

    if (!response.ok) {
      const body = await response.json().catch(() => null);
      const message = body && Array.isArray(body.errors) && body.errors.length > 0
        ? body.errors.join(" ")
        : (body && body.error) || "Could not create account.";
      errorNode.textContent = message;
      errorNode.hidden = false;
      // A rejected Turnstile token is single-use/expired either way - reset
      // the widget so the next attempt has to solve a fresh challenge.
      if (turnstileEnabled && turnstileWidgetId !== null && window.turnstile) {
        window.turnstile.reset(turnstileWidgetId);
        turnstileToken = "";
      }
      return;
    }

    window.location.href = "dashboard.html";
  } catch (err) {
    errorNode.textContent = "Could not reach the server. Please try again.";
    errorNode.hidden = false;
  } finally {
    document.getElementById("registerBtn").disabled = turnstileEnabled && !turnstileToken;
    document.getElementById("registerLoading").hidden = true;
  }
});
