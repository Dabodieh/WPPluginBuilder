(async () => {
  try {
    const meResponse = await apiFetch("/api/account/me");
    if (!meResponse.ok) {
      window.location.href = "login.html";
      return;
    }
    document.getElementById("account").hidden = false;

    const response = await apiFetch("/api/account");
    if (!response.ok) throw new Error("Account unavailable");
    const account = await response.json();

    document.getElementById("accountEmail").textContent = account.email || "";
    const verifiedNode = document.getElementById("verifiedStatus");
    const resendBtn = document.getElementById("resendVerificationBtn");
    if (account.emailConfirmed) {
      verifiedNode.textContent = "Verified ✓";
    } else {
      verifiedNode.textContent = "Verification required";
      resendBtn.hidden = false;
    }
  } catch (err) {
    document.getElementById("accountEmail").textContent = "Unavailable. Please refresh.";
  }

  document.getElementById("resendVerificationBtn").addEventListener("click", async () => {
    const btn = document.getElementById("resendVerificationBtn");
    const messageNode = document.getElementById("resendMessage");
    btn.disabled = true;
    try {
      const resendResponse = await apiFetch("/api/account/resend-verification", { method: "POST" });
      const body = await resendResponse.json().catch(() => null);
      messageNode.textContent = (body && body.message) || "If verification is required, a new verification email has been sent.";
    } catch {
      messageNode.textContent = "Could not reach the server. Please try again.";
    } finally {
      messageNode.hidden = false;
      btn.disabled = false;
    }
  });

  try {
    const policyResponse = await apiFetch("/api/account/password-policy");
    if (policyResponse.ok) {
      const policy = await policyResponse.json();
      const parts = [`At least ${policy.requiredLength} characters`];
      if (policy.requireUppercase) parts.push("an uppercase letter");
      if (policy.requireLowercase) parts.push("a lowercase letter");
      if (policy.requireDigit) parts.push("a number");
      if (policy.requireNonAlphanumeric) parts.push("a symbol");
      document.getElementById("passwordHint").textContent = parts.join(", ") + ".";
    }
  } catch {
    document.getElementById("passwordHint").hidden = true;
  }

  document.getElementById("changePasswordForm").addEventListener("submit", async (e) => {
    e.preventDefault();
    const noticeNode = document.getElementById("passwordFormNotice");
    const errorNode = document.getElementById("passwordFormError");
    noticeNode.hidden = true;
    errorNode.hidden = true;

    const currentPassword = document.getElementById("currentPassword").value;
    const newPassword = document.getElementById("newPassword").value;
    const confirmPassword = document.getElementById("confirmNewPassword").value;

    const btn = document.getElementById("changePasswordBtn");
    btn.disabled = true;
    try {
      const response = await apiFetch("/api/account/change-password", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ currentPassword, newPassword, confirmPassword }),
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
          : (body && body.error) || "Could not change your password.";
        errorNode.textContent = message;
        errorNode.hidden = false;
        return;
      }

      noticeNode.textContent = "Password updated successfully.";
      noticeNode.hidden = false;
      document.getElementById("changePasswordForm").reset();
    } catch (err) {
      errorNode.textContent = "Could not reach the server. Please try again.";
      errorNode.hidden = false;
    } finally {
      btn.disabled = false;
    }
  });

  document.getElementById("changeEmailForm").addEventListener("submit", async (e) => {
    e.preventDefault();
    const noticeNode = document.getElementById("emailFormNotice");
    const errorNode = document.getElementById("emailFormError");
    noticeNode.hidden = true;
    errorNode.hidden = true;

    const newEmail = document.getElementById("newEmail").value;
    const currentPassword = document.getElementById("emailCurrentPassword").value;

    const btn = document.getElementById("changeEmailBtn");
    btn.disabled = true;
    try {
      const response = await apiFetch("/api/account/change-email", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ newEmail, currentPassword }),
      });

      if (response.status === 429) {
        errorNode.textContent = "Too many requests. Please try again in a few minutes.";
        errorNode.hidden = false;
        return;
      }

      const body = await response.json().catch(() => null);
      if (!response.ok) {
        errorNode.textContent = (body && body.error) || "Could not start the email change.";
        errorNode.hidden = false;
        return;
      }

      noticeNode.textContent = (body && body.message) || "Check your new email address for a confirmation link.";
      noticeNode.hidden = false;
      document.getElementById("changeEmailForm").reset();
    } catch (err) {
      errorNode.textContent = "Could not reach the server. Please try again.";
      errorNode.hidden = false;
    } finally {
      btn.disabled = false;
    }
  });

  document.getElementById("signOutAllBtn").addEventListener("click", () => {
    document.getElementById("signOutAllConfirm").hidden = false;
    document.getElementById("signOutAllBtn").hidden = true;
  });

  document.getElementById("signOutAllCancelBtn").addEventListener("click", () => {
    document.getElementById("signOutAllConfirm").hidden = true;
    document.getElementById("signOutAllBtn").hidden = false;
  });

  document.getElementById("signOutAllConfirmBtn").addEventListener("click", async () => {
    const noticeNode = document.getElementById("signOutAllNotice");
    const errorNode = document.getElementById("signOutAllError");
    noticeNode.hidden = true;
    errorNode.hidden = true;

    const btn = document.getElementById("signOutAllConfirmBtn");
    btn.disabled = true;
    try {
      const response = await apiFetch("/api/account/signout-all", { method: "POST" });
      if (!response.ok) {
        errorNode.textContent = "Could not sign out of all sessions. Please try again.";
        errorNode.hidden = false;
        return;
      }
      window.location.href = "login.html";
    } catch (err) {
      errorNode.textContent = "Could not reach the server. Please try again.";
      errorNode.hidden = false;
    } finally {
      btn.disabled = false;
    }
  });
})();
