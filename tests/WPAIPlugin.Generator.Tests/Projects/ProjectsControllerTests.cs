using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WPAIPlugin.Api.Validation;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Projects;

public class ProjectsControllerTests : IClassFixture<ProjectsTestFactory>
{
    private readonly ProjectsTestFactory _factory;

    public ProjectsControllerTests(ProjectsTestFactory factory)
    {
        _factory = factory;
        _factory.FakeValidator.Handler = null;
    }

    private static HttpClient NewClient(ProjectsTestFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static async Task<HttpClient> RegisterAndLoginAsync(ProjectsTestFactory factory)
    {
        var client = NewClient(factory);
        var email = $"user-{Guid.NewGuid()}@example.com";
        var response = await client.PostAsJsonAsync("/api/account/register", new { email, password = "Str0ng!Passw0rd" });
        response.EnsureSuccessStatusCode();
        return client;
    }

    private static object ValidSpecPayload(bool validated = false) => new
    {
        spec = new
        {
            name = "Staff Directory",
            slug = "staff-directory",
            description = "Simple staff directory plugin",
            version = "1.0.0",
            author = "AI Plugin Builder",
            features = new[] { "shortcode" },
        },
        validated,
    };

    [Fact]
    public async Task Plan_Authenticated_Succeeds()
    {
        var client = await RegisterAndLoginAsync(_factory);

        var response = await client.PostAsJsonAsync("/api/plugins/plan", new { description = "Create a staff directory" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Build_Unauthenticated_ReturnsUnauthorized()
    {
        var client = NewClient(_factory);

        var response = await client.PostAsJsonAsync("/api/projects/build", ValidSpecPayload());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Build_Authenticated_CreatesProjectAndVersion1()
    {
        var client = await RegisterAndLoginAsync(_factory);

        var response = await client.PostAsJsonAsync("/api/projects/build", ValidSpecPayload());
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, body.GetProperty("revisionNumber").GetInt32());
        Assert.False(body.GetProperty("validated").GetBoolean());
        Assert.True(body.TryGetProperty("projectId", out _));
        Assert.True(body.TryGetProperty("versionId", out _));
    }

    [Fact]
    public async Task Build_Authenticated_ArtifactIsSavedAndDownloadable()
    {
        var client = await RegisterAndLoginAsync(_factory);

        var buildResponse = await client.PostAsJsonAsync("/api/projects/build", ValidSpecPayload());
        var build = await buildResponse.Content.ReadFromJsonAsync<JsonElement>();
        var downloadUrl = build.GetProperty("downloadUrl").GetString();

        var downloadResponse = await client.GetAsync(downloadUrl);

        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        Assert.Equal("application/zip", downloadResponse.Content.Headers.ContentType?.MediaType);
        var bytes = await downloadResponse.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public async Task Build_ThenList_ProjectAppearsInMyPlugins()
    {
        var client = await RegisterAndLoginAsync(_factory);
        await client.PostAsJsonAsync("/api/projects/build", ValidSpecPayload());

        var response = await client.GetAsync("/api/projects");
        var projects = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, projects.GetArrayLength());
        Assert.Equal("Staff Directory", projects[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task SecondUser_CannotSeeFirstUsersProject()
    {
        var userA = await RegisterAndLoginAsync(_factory);
        await userA.PostAsJsonAsync("/api/projects/build", ValidSpecPayload());

        var userB = await RegisterAndLoginAsync(_factory);
        var response = await userB.GetAsync("/api/projects");
        var projects = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0, projects.GetArrayLength());
    }

    [Fact]
    public async Task SecondUser_CannotOpenFirstUsersProjectById()
    {
        var userA = await RegisterAndLoginAsync(_factory);
        var buildResponse = await userA.PostAsJsonAsync("/api/projects/build", ValidSpecPayload());
        var build = await buildResponse.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = build.GetProperty("projectId").GetString();

        var userB = await RegisterAndLoginAsync(_factory);
        var response = await userB.GetAsync($"/api/projects/{projectId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SecondUser_CannotDownloadFirstUsersZip_EvenWithExactGuessedIds()
    {
        var userA = await RegisterAndLoginAsync(_factory);
        var buildResponse = await userA.PostAsJsonAsync("/api/projects/build", ValidSpecPayload());
        var build = await buildResponse.Content.ReadFromJsonAsync<JsonElement>();
        var downloadUrl = build.GetProperty("downloadUrl").GetString();

        var userB = await RegisterAndLoginAsync(_factory);
        var response = await userB.GetAsync(downloadUrl);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Download_UnknownProjectOrVersionId_ReturnsNotFound()
    {
        var client = await RegisterAndLoginAsync(_factory);

        var response = await client.GetAsync($"/api/projects/{Guid.NewGuid()}/versions/{Guid.NewGuid()}/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BuildResponse_NeverContainsArtifactKeyOrFilesystemPath()
    {
        var client = await RegisterAndLoginAsync(_factory);

        var response = await client.PostAsJsonAsync("/api/projects/build", ValidSpecPayload());
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("ArtifactKey", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UserId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_factory.ArtifactRoot, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":\\", body);
    }

    [Fact]
    public async Task ProjectDetailResponse_NeverContainsArtifactKeyOrUserId()
    {
        var client = await RegisterAndLoginAsync(_factory);
        var buildResponse = await client.PostAsJsonAsync("/api/projects/build", ValidSpecPayload());
        var build = await buildResponse.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = build.GetProperty("projectId").GetString();

        var response = await client.GetAsync($"/api/projects/{projectId}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("ArtifactKey", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UserId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_factory.ArtifactRoot, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Build_ValidationFails_DoesNotSaveVersion()
    {
        _factory.FakeValidator.Handler = (_, args) =>
            args.Contains("php")
                ? new DockerPluginValidator.ProcessResult(1, string.Empty, "syntax error")
                : new DockerPluginValidator.ProcessResult(0, string.Empty, string.Empty);

        var client = await RegisterAndLoginAsync(_factory);

        var buildResponse = await client.PostAsJsonAsync("/api/projects/build", ValidSpecPayload(validated: true));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, buildResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/projects");
        var projects = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, projects.GetArrayLength());

        _factory.FakeValidator.Handler = null;
    }
}
