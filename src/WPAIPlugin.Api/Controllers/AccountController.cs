using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Credits;

namespace WPAIPlugin.Api.Controllers;

// Minimal ASP.NET Core Identity account endpoints (Milestone 10): register,
// login, logout, current-user. No email verification, MFA, or password
// reset yet - deliberately out of scope for this milestone.
[ApiController]
[Route("api/account")]
public sealed class AccountController : ControllerBase
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly CreditService _creditService;
    private readonly CreditOptions _creditOptions;
    private readonly AppDbContext _db;

    public AccountController(
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        CreditService creditService,
        IOptions<CreditOptions> creditOptions, AppDbContext db)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _creditService = creditService;
        _creditOptions = creditOptions.Value;
        _db = db;
    }

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
            await _creditService.GrantSignupCreditsAsync(user.Id, _creditOptions.SignupGrant);
            if (transaction is not null) await transaction.CommitAsync(CancellationToken.None);
        }
        catch
        {
            _db.ChangeTracker.Clear();
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            else
            {
                // The test provider has no transactions; compensate Identity
                // creation if the grant failed before saving.
                var cleanup = await _userManager.DeleteAsync(user);
                if (!cleanup.Succeeded) throw new InvalidOperationException("Registration cleanup failed.");
            }
            return StatusCode(503, new { error = "Registration could not be completed. Please try again." });
        }

        await _signInManager.SignInAsync(user, isPersistent: true);
        return Ok(new { email = user.Email });
    }

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
    public IActionResult Me()
    {
        return Ok(new { email = User.Identity?.Name });
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
