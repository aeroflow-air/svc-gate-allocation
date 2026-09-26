using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace AeroFlow.GateAllocation.Tests.Api;

/// <summary>HTTP edge tests with a fixed clock. Each test gets a fresh host, so store mutations don't leak between tests.</summary>
public sealed class GateAllocationApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);
    private readonly WebApplicationFactory<Program> _factory;

    public GateAllocationApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now))))
        .CreateClient();

    [Fact]
    public async Task Ping_uses_the_injected_clock()
    {
        using var client = CreateClient();

        using var body = await GetJson(client, "/api/gates/ping");

        Assert.Equal(Now, body.RootElement.GetProperty("utcNow").GetDateTimeOffset());
    }

    [Fact]
    public async Task Gates_are_listed_by_id_with_status_and_allocations()
    {
        using var client = CreateClient();

        using var body = await GetJson(client, "/api/gates");

        var gates = body.RootElement.EnumerateArray().ToList();
        Assert.Equal(["12", "14", "21", "55A", "7", "9", "R1"], gates.Select(g => g.GetProperty("id").GetString()));

        var gate12 = gates.Single(g => g.GetProperty("id").GetString() == "12");
        Assert.Equal(["AF204", "AF455"], gate12.GetProperty("allocations").EnumerateArray().Select(a => a.GetProperty("flightNumber").GetString()));

        var r1 = gates.Single(g => g.GetProperty("id").GetString() == "R1");
        Assert.Equal("Closed", r1.GetProperty("status").GetString());
        Assert.Equal("Resurfacing", r1.GetProperty("closedReason").GetString());
    }

    [Fact]
    public async Task Demo_auto_allocation_used_the_smallest_free_gate()
    {
        using var client = CreateClient();

        using var body = await GetJson(client, "/api/allocations/af733");

        Assert.Equal("21", body.RootElement.GetProperty("gate").GetString());
    }

    [Fact]
    public async Task Posting_without_a_gate_auto_allocates_and_returns_201()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/allocations", new
        {
            flightNumber = "AF100",
            aircraftSize = "e",
            onBlock = Now.AddHours(6),
            offBlock = Now.AddHours(7),
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith("/api/allocations/AF100", response.Headers.Location?.ToString());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("14", body.RootElement.GetProperty("gate").GetString());
        Assert.Equal("E", body.RootElement.GetProperty("aircraftSize").GetString());
    }

    [Fact]
    public async Task Posting_to_a_busy_gate_returns_409_gate_conflict()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/allocations", new
        {
            flightNumber = "AF100",
            aircraftSize = "C",
            onBlock = Now,
            offBlock = Now.AddMinutes(30),
            gate = "12",
        });

        await AssertProblem(response, HttpStatusCode.Conflict, "gate_conflict");
    }

    [Theory]
    [InlineData("{\"aircraftSize\":\"C\",\"onBlock\":\"2026-09-25T10:00:00Z\",\"offBlock\":\"2026-09-25T11:00:00Z\"}", "invalid_flight_number")]
    [InlineData("{\"flightNumber\":\"AF100\",\"aircraftSize\":\"Z\",\"onBlock\":\"2026-09-25T10:00:00Z\",\"offBlock\":\"2026-09-25T11:00:00Z\"}", "invalid_aircraft_size")]
    [InlineData("{\"flightNumber\":\"AF100\",\"aircraftSize\":\"C\",\"onBlock\":\"2026-09-25T10:00:00Z\"}", "invalid_request")]
    [InlineData("{\"flightNumber\":\"AF100\",\"aircraftSize\":\"C\",\"onBlock\":\"2026-09-25T11:00:00Z\",\"offBlock\":\"2026-09-25T10:00:00Z\"}", "empty_time_window")]
    public async Task Malformed_allocation_returns_400_with_code(string json, string code)
    {
        using var client = CreateClient();

        var response = await client.PostAsync("/api/allocations", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        await AssertProblem(response, HttpStatusCode.BadRequest, code);
    }

    [Fact]
    public async Task Reassign_moves_the_flight()
    {
        using var client = CreateClient();

        var response = await client.PutAsJsonAsync("/api/allocations/AF204/gate", new { gate = "9" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var reread = await GetJson(client, "/api/allocations/AF204");
        Assert.Equal("9", reread.RootElement.GetProperty("gate").GetString());
    }

    [Fact]
    public async Task Retime_that_clashes_with_the_next_flight_returns_409()
    {
        using var client = CreateClient();

        // AF455 is on gate 12 from +75 min; stretching AF204 to +70 leaves less than the 15 min turnaround.
        var response = await client.PutAsJsonAsync("/api/allocations/AF204/window", new { onBlock = Now.AddMinutes(-5), offBlock = Now.AddMinutes(70) });

        await AssertProblem(response, HttpStatusCode.Conflict, "gate_conflict");
    }

    [Fact]
    public async Task Release_returns_204_then_the_allocation_is_gone()
    {
        using var client = CreateClient();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/allocations/AF204")).StatusCode);

        await AssertProblem(await client.GetAsync("/api/allocations/AF204"), HttpStatusCode.NotFound, "allocation_not_found");
    }

    [Fact]
    public async Task Closing_a_gate_in_use_returns_409_and_a_free_one_closes()
    {
        using var client = CreateClient();

        await AssertProblem(await client.PostAsJsonAsync("/api/gates/12/close", new { reason = "Fault" }), HttpStatusCode.Conflict, "gate_has_allocations");

        var response = await client.PostAsJsonAsync("/api/gates/7/close", new { reason = "Fault" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Closed", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Reopening_a_closed_gate_clears_the_reason()
    {
        using var client = CreateClient();

        var response = await client.PostAsync("/api/gates/r1/reopen", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Open", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("closedReason").ValueKind);
    }

    [Fact]
    public async Task Unknown_gate_returns_404_and_bad_gate_returns_400()
    {
        using var client = CreateClient();

        await AssertProblem(await client.GetAsync("/api/gates/99"), HttpStatusCode.NotFound, "gate_not_found");
        await AssertProblem(await client.GetAsync("/api/gates/12345"), HttpStatusCode.BadRequest, "invalid_gate");
    }

    private static async Task<JsonDocument> GetJson(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }
}
