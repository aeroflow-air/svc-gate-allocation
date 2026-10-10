using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AeroFlow.GateAllocation.Tests;

/// <summary>The test host runs as Development, where ApiDocs:Enabled is true.</summary>
public sealed class ApiDocsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiDocsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task OpenApi_document_lists_every_endpoint()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var docPaths = doc.RootElement.GetProperty("paths");
        Assert.True(docPaths.TryGetProperty("/api/gates/ping", out _), "missing path /api/gates/ping");
        Assert.True(docPaths.TryGetProperty("/api/gates", out _), "missing path /api/gates");
        Assert.True(docPaths.TryGetProperty("/api/gates/{gate}", out _), "missing path /api/gates/{gate}");
        Assert.True(docPaths.TryGetProperty("/api/gates/{gate}/close", out _), "missing path /api/gates/{gate}/close");
        Assert.True(docPaths.TryGetProperty("/api/gates/{gate}/reopen", out _), "missing path /api/gates/{gate}/reopen");
        Assert.True(docPaths.TryGetProperty("/api/allocations", out _), "missing path /api/allocations");
        Assert.True(docPaths.TryGetProperty("/api/allocations/{flightNumber}", out _), "missing path /api/allocations/{flightNumber}");
        Assert.True(docPaths.TryGetProperty("/api/allocations/{flightNumber}/gate", out _), "missing path /api/allocations/{flightNumber}/gate");
        Assert.True(docPaths.TryGetProperty("/api/allocations/{flightNumber}/window", out _), "missing path /api/allocations/{flightNumber}/window");
    }

    [Fact]
    public async Task Scalar_ui_is_served()
    {
        var response = await _client.GetAsync("/scalar");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }
}
