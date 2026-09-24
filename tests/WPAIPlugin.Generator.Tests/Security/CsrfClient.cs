using System.Net.Http.Json;
using System.Text.Json;

namespace WPAIPlugin.Generator.Tests;

// Existing HTTP tests follow the same real token exchange as the browser.
internal static class CsrfClient
{
    public static async Task<HttpResponseMessage> PostJsonWithCsrfAsync<T>(this HttpClient client, string uri, T value)
    {
        return await client.PostWithCsrfAsync(uri, JsonContent.Create(value));
    }

    public static async Task<HttpResponseMessage> PostWithCsrfAsync(this HttpClient client, string uri, HttpContent? content)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/account/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> PutAsJsonWithCsrfAsync<T>(this HttpClient client, string uri, T value)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/account/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Put, uri) { Content = JsonContent.Create(value) };
        request.Headers.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
}

