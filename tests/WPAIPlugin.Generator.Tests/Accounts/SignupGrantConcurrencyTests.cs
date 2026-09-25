using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WPAIPlugin.Api.Data;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Accounts;

/// <summary>
/// Proves the one-time signup credit/free-build grant survives replay, and
/// that a duplicate/racing registration attempt never reveals the address
/// already has an account (signup-farming + account-enumeration hardening
/// milestones). Registration is the only place either grant happens - email
/// confirmation, resend, login, password reset, and email change never
/// touch CreditService/BuildEntitlementService (verified by inspection: grep
/// for GrantSignupCreditsAsync/SignupFreeBuildGrant across src/ turns up
/// exactly one call site each, both in AccountController.Register).
///
/// This class covers the SEQUENTIAL replay case, which EF Core's InMemory
/// provider can prove correctly. A genuinely CONCURRENT duplicate-
/// registration race depends on PostgreSQL's own unique index
/// (AspNetUsers.UserNameIndex) throwing on the losing insert - InMemory does
/// not reproduce that behaviour under parallel writes from separate
/// DbContext instances (confirmed empirically: an equivalent
/// Task.WhenAll-based test against this same InMemory provider let all N
/// concurrent registration attempts "succeed" at the Identity-account level,
/// which is not how PostgreSQL behaves and would misrepresent the real
/// guarantee either way it happened to come out). That race was instead
/// verified live against a real PostgreSQL instance:
///
///   10 fully concurrent POST /api/account/register requests for the same
///   email against docker/docker-compose.saas.yml's real Postgres db
///   produced 10×200, every response body byte-identical (the same
///   enumeration-safe generic message - see AccountController.
///   RegistrationAccepted), zero 500s, and internally exactly one
///   AspNetUsers row, one CreditAccount, one BuildEntitlementAccount, and
///   one SignupGrant/SignupFreeBuildGrant ledger transaction each. The
///   losing 9 requests hit AccountController.Register's
///   catch (DbUpdateException) block: UserManager.CreateAsync's own
///   application-level uniqueness check is a query, not atomic with its
///   insert, so a losing concurrent request only fails at the database's
///   real unique-index level, inside CreateAsync itself, after Identity's
///   own validator already said "ok". That branch, and the sibling
///   DuplicateUserName/DuplicateEmail branch below it, both return the same
///   RegistrationAccepted() response a real success does - never "already
///   taken", never the submitted email, never a database/Identity detail.
/// </summary>
public class SignupGrantConcurrencyTests
{
    [Fact]
    public async Task SequentialDuplicateRegistration_SameResponseWithoutSecondGrant()
    {
        using var factory = new AccountTestFactory();
        var client = factory.CreateClient();
        var email = $"sequential-signup-{Guid.NewGuid()}@example.com";
        var body = new { email, password = "Str0ng!Passw0rd" };

        var first = await client.PostJsonWithCsrfAsync("/api/account/register", body);
        var second = await client.PostJsonWithCsrfAsync("/api/account/register", body);

        // Enumeration-safe: both responses are OK with identical bodies -
        // the ledger assertions below are what actually prove only one
        // grant happened, not the HTTP status/body.
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.Equal(await first.Content.ReadAsStringAsync(), secondBody);
        Assert.DoesNotContain(email, secondBody);
        Assert.DoesNotContain("already", secondBody, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);

        Assert.Equal(1, await db.CreditAccounts.CountAsync(a => a.UserId == user.Id));
        Assert.Equal(1, await db.CreditTransactions.CountAsync(
            t => t.UserId == user.Id && t.Type == CreditTransactionType.SignupGrant));
        Assert.Equal(1, await db.BuildEntitlementAccounts.CountAsync(a => a.UserId == user.Id));
        Assert.Equal(1, await db.BuildEntitlementTransactions.CountAsync(
            t => t.UserId == user.Id && t.Type == BuildEntitlementTransactionType.SignupFreeBuildGrant));
    }

    [Fact]
    public async Task ConcurrentDuplicateRegistration_NeverReturns500_AllResponsesIdenticalAndSafe()
    {
        // InMemory can't prove the *database*-level race is closed (see class
        // remarks - that's the live-Postgres-verified part), but it can
        // still prove the application-level outcome that matters to a
        // caller: no unhandled exception, and every response - winner or
        // loser - is the same enumeration-safe shape.
        using var factory = new AccountTestFactory();
        var email = $"concurrent-signup-{Guid.NewGuid()}@example.com";
        const int attempts = 10;
        var clients = Enumerable.Range(0, attempts).Select(_ => factory.CreateClient()).ToList();

        var responses = await Task.WhenAll(clients.Select(c =>
            c.PostJsonWithCsrfAsync("/api/account/register", new { email, password = "Str0ng!Passw0rd" })));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
        Assert.All(bodies, b => Assert.Equal(bodies[0], b));
        Assert.All(bodies, b => Assert.DoesNotContain(email, b));
        Assert.All(bodies, b => Assert.DoesNotContain("already", b, StringComparison.OrdinalIgnoreCase));
    }
}
