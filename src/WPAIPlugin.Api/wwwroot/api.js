// Standard ASP.NET antiforgery token, scoped to the current identity.
// Fetch a fresh token before writes so login/logout changes cannot leave a
// stale identity-bound token in a long-lived page or another browser tab.
async function apiFetch(url, options = {}) {
  const method = (options.method || "GET").toUpperCase();
  if (!["GET", "HEAD", "OPTIONS"].includes(method)) {
    const response = await fetch("/api/account/csrf", { cache: "no-store" });
    if (!response.ok) throw new Error("Could not prepare secure request.");
    const body = await response.json();
    const headers = new Headers(options.headers);
    headers.set("X-CSRF-TOKEN", body.token);
    options = { ...options, headers };
  }
  return fetch(url, options);
}
