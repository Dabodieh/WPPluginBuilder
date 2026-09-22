using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using WPAIPlugin.Api.Controllers;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Generator.Models;
using WPAIPlugin.Generator.Tests.Projects;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Credits;

public class CreditBuildBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOrGenerationFailureAfterChargeRefunds(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var spec = new PluginSpec { Name = "Test", Slug = "test", Description = "Test",
            Version = "1.0.0", Author = "Test", Features = new() { "shortcode" } };
        using var factory = new ProjectsTestFactory();
        var interceptor = new AfterCharge(() =>
        {
            if (cancel) cancellation.Cancel();
            else spec.Slug = "invalid slug"; // Generator failure after controller pre-validation.
        });
        factory.ConfigureDatabase = o => o.AddInterceptors(interceptor);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new IdentityUser { Id = "test-user", UserName = "test" });
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<WPAIPlugin.Api.Credits.CreditService>().GrantSignupCreditsAsync("test-user", 100);
        var controller = ActivatorUtilities.CreateInstance<ProjectsController>(scope.ServiceProvider);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "test-user") }, "test")),
            RequestAborted = cancellation.Token,
        } };
        if (cancel)
            await Assert.ThrowsAsync<OperationCanceledException>(() => controller.Build(new ProjectBuildRequest { Spec = spec }, cancellation.Token));
        else
            Assert.Equal(500, Assert.IsType<ObjectResult>(await controller.Build(new ProjectBuildRequest { Spec = spec }, cancellation.Token)).StatusCode);
        Assert.True(interceptor.Triggered);
        Assert.Equal(100, await db.CreditAccounts.Select(a => a.Balance).SingleAsync());
        Assert.Single(await db.CreditTransactions.Where(t => t.Type == CreditTransactionType.Refund).ToListAsync());
        Assert.Equal(100, await db.CreditTransactions.SumAsync(t => t.Amount));
        Assert.Empty(db.PluginProjects);
    }

    private sealed class AfterCharge(Action action) : SaveChangesInterceptor
    {
        public bool Triggered;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!Triggered && eventData.Context!.ChangeTracker.Entries<CreditTransaction>().Any(e => e.Entity.Amount < 0))
            {
                Triggered = true;
                action();
            }
            return ValueTask.FromResult(result);
        }
    }
}

