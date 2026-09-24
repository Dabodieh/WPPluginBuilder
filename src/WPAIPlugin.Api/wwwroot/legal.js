// Fills the configured support email into any element carrying
// data-support-email (text content) or data-support-mailto (mailto href).
// The server is the only source of truth for this value - never hard-coded.
(async () => {
  try {
    const response = await apiFetch("/api/config/public");
    if (!response.ok) throw new Error("Config unavailable");
    const config = await response.json();
    const email = config.supportEmail;
    if (!email) return;
    document.querySelectorAll("[data-support-email]").forEach((el) => { el.textContent = email; });
    document.querySelectorAll("[data-support-mailto]").forEach((el) => {
      el.href = `mailto:${email}`;
      el.textContent = email;
    });
  } catch {
    document.querySelectorAll("[data-support-email]").forEach((el) => { el.textContent = "our support address"; });
  }
})();
