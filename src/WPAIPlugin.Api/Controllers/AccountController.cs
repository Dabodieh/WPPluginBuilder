using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using WPAIPlugin.Api.Configuration;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Credits;
using WPAIPlugin.Api.Email;
using WPAIPlugin.Api.Entitlements;
using WPAIPlugin.Api.Promotions;
using WPAIPlugin.Api.Security;

namespace WPAIPlugin.Api.Controllers;

// Minimal ASP.NET Core Identity account endpoints (Milestone 10): register,
// login, logout, current-user. Forgot/reset password added in the
// account-recovery milestone - see ForgotPassword/ResetPassword below. No
// email verification or MFA yet - still out of scope.
[ApiController]
[Route("api/account")]
[AutoValidateAntiforgeryToken]
[RequestSizeLimit(16 * 1024)]
public sealed class AccountController : ControllerBase
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly CreditService _creditService;
    private readonly CreditOptions _creditOptions;
    private readonly BuildEntitlementService _entitlementService;
    private readonly PromotionsOptions _promotionsOptions;
    private readonly ITransactionalEmailSender _emailSender;
    private readonly AppOptions _appOptions;
    private readonly AppDbContext _db;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        CreditService creditService,
        IOptions<CreditOptions> creditOptions,
        BuildEntitlementService entitlementService,
        IOptions<PromotionsOptions> promotionsOptions,
        ITransactionalEmailSender emailSender,
        IOptions<AppOptions> appOptions,
        AppDbContext db,
        ILogger<AccountController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _creditService = creditService;
        _creditOptions = creditOptions.Value;
        _entitlementService = entitlementService;
        _promotionsOptions = promotionsOptions.Value;
        _emailSender = emailSender;
        _appOptions = appOptions.Value;
        _db = db;
        _logger = logger;
    }

    [HttpGet("csrf")]
    public IActionResult Csrf([FromServices] IAntiforgery antiforgery) =>
        Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [EnableRateLimiting("account")]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Email and password are required." });
        }

        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(CancellationToken.None) : null;
        var user = new IdentityUser { UserName = request.Email, Email = request.Email };
        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        try
        {
            // Two distinct new-account benefits (Promotions + Free Builds
            // milestone): signup credits and free-build entitlements. Each
            // happens exactly once per registration, in the same relational
            // transaction as the Identity account itself. Granted immediately
            // regardless of email-verification status - they simply cannot be
            // consumed by an unverified account (enforced server-side at the
            // planning/build endpoints), so there is no abuse exposure in
            // granting them up front.
            await _creditService.GrantSignupCreditsAsync(user.Id, _creditOptions.SignupGrant);
            if (_promotionsOptions.SignupFreeBuilds > 0)
            {
                await _entitlementService.GrantAsync(
                    user.Id, _promotionsOptions.SignupFreeBuilds,
                    BuildEntitlementTransactionType.SignupFreeBuildGrant, $"signup:{Guid.NewGuid()}");
            }
            if (transaction is not null) await transaction.CommitAsync(CancellationToken.None);
        }
        catch
        {
            _db.ChangeTracker.Clear();
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            else
            {
                // The test provider has no transactions, and each grant above
                // is its own already-committed SaveChanges call - compensate
                // every partial write by hand before deleting the Identity
                // account, since InMemory has no rollback to rely on.
                var creditAccount = await _db.CreditAccounts.FindAsync([user.Id], CancellationToken.None);
                if (creditAccount is not null) _db.CreditAccounts.Remove(creditAccount);
                var creditTransactions = await _db.CreditTransactions.Where(t => t.UserId == user.Id).ToListAsync(CancellationToken.None);
                _db.CreditTransactions.RemoveRange(creditTransactions);
                var entitlementAccount = await _db.BuildEntitlementAccounts.FindAsync([user.Id], CancellationToken.None);
                if (entitlementAccount is not null) _db.BuildEntitlementAccounts.Remove(entitlementAccount);
                var entitlementTransactions = await _db.BuildEntitlementTransactions.Where(t => t.UserId == user.Id).ToListAsync(CancellationToken.None);
                _db.BuildEntitlementTransactions.RemoveRange(entitlementTransactions);
                if (creditAccount is not null || creditTransactions.Count > 0 || entitlementAccount is not null || entitlementTransactions.Count > 0)
                    await _db.SaveChangesAsync(CancellationToken.None);

                var cleanup = await _userManager.DeleteAsync(user);
                if (!cleanup.Succeeded) throw new InvalidOperationException("Registration cleanup failed.");
            }
            return StatusCode(503, new { error = "Registration could not be completed. Please try again." });
        }

        // Confirmation token generated after grants/sign-in are safely
        // committed - a send failure here must never undo a successful
        // registration. The account remains usable via login/resend even if
        // this first email never arrives.
        try
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var confirmUrl = BuildConfirmEmailUrl(user.Email!, token);
            await _emailSender.SendEmailConfirmationAsync(user.Email!, confirmUrl, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError("Email confirmation send failed after registration. Failure category: {FailureType}.", ex.GetType().Name);
        }

        await _signInManager.SignInAsync(user, isPersistent: true);
        return Ok(new
        {
            email = user.Email,
            message = "Your account has been created. Check your email to verify your address before creating plugins.",
        });
    }

    [EnableRateLimiting("account")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Invalid email or password." });
        }

        var result = await _signInManager.PasswordSignInAsync(
            request.Email, request.Password, isPersistent: true, lockoutOnFailure: false);

        if (!result.Succeeded)
        {
            return Unauthorized(new { error = "Invalid email or password." });
        }

        return Ok(new { email = request.Email });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return Ok();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await _userManager.GetUserAsync(User);
        return Ok(new
        {
            email = User.Identity?.Name,
            isAdmin = User.IsInRole(AdminAuthorization.AdminRole),
            emailConfirmed = user?.EmailConfirmed ?? false,
        });
    }

    /// <summary>
    /// Always returns the same generic response, whether or not the email
    /// belongs to a registered account - never reveals account existence,
    /// lockout status, or any other account state through the response.
    /// A matching account gets a real Identity reset token and a real email;
    /// an unknown email causes no side effect at all.
    /// </summary>
    [EnableRateLimiting("passwordRecovery")]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        const string genericMessage = "If an account exists for that email address, a password reset link has been sent.";

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user is not null && user.Email is not null)
            {
                try
                {
                    var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                    var resetUrl = BuildResetUrl(user.Email, token);
                    await _emailSender.SendPasswordResetEmailAsync(user.Email, resetUrl, cancellationToken);
                }
                catch
                {
                    // Never let a send failure change the response shape - that
                    // would reveal, during an email-service outage, which
                    // addresses are real accounts. Already logged safely inside
                    // the email sender itself.
                }
            }
        }

        return Ok(new { message = genericMessage });
    }

    /// <summary>
    /// Validates the Identity-issued reset token (never a custom token store)
    /// and resets the password via UserManager. Returns the same generic
    /// "invalid or expired" message for an unknown email and for a genuinely
    /// invalid/expired token, so neither response distinguishes the two.
    /// </summary>
    [EnableRateLimiting("passwordRecovery")]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        const string invalidLinkMessage = "This reset link is invalid or has expired. Please request a new one.";

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Token)
            || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new { error = "Email, reset token, and a new password are required." });
        }

        if (request.NewPassword != request.ConfirmPassword)
        {
            return BadRequest(new { error = "Passwords do not match." });
        }

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return BadRequest(new { error = invalidLinkMessage });
        }

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code is "InvalidToken"))
            {
                return BadRequest(new { error = invalidLinkMessage });
            }
            // Password-policy errors are the same safe, descriptive text
            // already shown at registration - never an internal Identity/
            // security detail.
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        }

        return Ok(new { message = "Your password has been reset. You can now log in." });
    }

    /// <summary>
    /// Confirms the Identity-issued email-confirmation token (never a custom
    /// token store). Returns the same safe generic failure message for an
    /// unknown email, an invalid/expired token, or a malformed request, so no
    /// response distinguishes them. An already-confirmed account is a
    /// harmless success, not an error - clicking an old confirmation link
    /// twice must never look like a failure.
    /// </summary>
    [EnableRateLimiting("passwordRecovery")]
    [HttpPost("confirm-email")]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailRequest request)
    {
        const string invalidLinkMessage = "This verification link is invalid or has expired. Please request a new one.";

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest(new { error = invalidLinkMessage });
        }

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return BadRequest(new { error = invalidLinkMessage });
        }

        if (user.EmailConfirmed)
        {
            return Ok(new { message = "Your email is already verified. You can create WordPress plugins." });
        }

        var result = await _userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
        {
            return BadRequest(new { error = invalidLinkMessage });
        }

        return Ok(new { message = "Your email has been verified. You can now create WordPress plugins." });
    }

    /// <summary>
    /// Authenticated-only, current-user-only (never a public endpoint
    /// accepting an arbitrary email address, which would make this an
    /// email-bombing/account-discovery vector). Always returns the same
    /// generic response regardless of confirmation state - never reveals
    /// whether the account was already verified.
    /// </summary>
    [Authorize]
    [EnableRateLimiting("emailVerificationResend")]
    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification(CancellationToken cancellationToken)
    {
        const string genericMessage = "If verification is required, a new verification email has been sent.";

        var user = await _userManager.GetUserAsync(User);
        if (user is not null && !user.EmailConfirmed && user.Email is not null)
        {
            try
            {
                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                var confirmUrl = BuildConfirmEmailUrl(user.Email, token);
                await _emailSender.SendEmailConfirmationAsync(user.Email, confirmUrl, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError("Resend email confirmation failed. Failure category: {FailureType}.", ex.GetType().Name);
            }
        }

        return Ok(new { message = genericMessage });
    }

    /// <summary>
    /// Built server-side from App:PublicBaseUrl only - request Host/scheme/
    /// any browser-supplied origin is never trusted for this, since an
    /// attacker who controlled it could redirect a real reset link to a
    /// hostile domain.
    /// </summary>
    private string BuildResetUrl(string email, string token)
    {
        var baseUrl = (_appOptions.PublicBaseUrl ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogError("App:PublicBaseUrl is not configured - cannot build a password reset URL.");
        }
        return $"{baseUrl}/reset-password.html?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
    }

    /// <summary>
    /// Same trusted-origin rule as BuildResetUrl - App:PublicBaseUrl only,
    /// never a browser-supplied Host/scheme/origin.
    /// </summary>
    private string BuildConfirmEmailUrl(string email, string token)
    {
        var baseUrl = (_appOptions.PublicBaseUrl ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogError("App:PublicBaseUrl is not configured - cannot build an email confirmation URL.");
        }
        return $"{baseUrl}/confirm-email.html?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
    }
}

public sealed class RegisterRequest
{
    public string? Email { get; set; }
    public string? Password { get; set; }
}

public sealed class LoginRequest
{
    public string? Email { get; set; }
    public string? Password { get; set; }
}

public sealed class ForgotPasswordRequest
{
    public string? Email { get; set; }
}

public sealed class ResetPasswordRequest
{
    public string? Email { get; set; }
    public string? Token { get; set; }
    public string? NewPassword { get; set; }
    public string? ConfirmPassword { get; set; }
}

public sealed class ConfirmEmailRequest
{
    public string? Email { get; set; }
    public string? Token { get; set; }
}
