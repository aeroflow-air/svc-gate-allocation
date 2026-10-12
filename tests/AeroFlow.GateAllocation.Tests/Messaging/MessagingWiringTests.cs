using AeroFlow.GateAllocation.Messaging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AeroFlow.GateAllocation.Tests.Messaging;

public sealed class MessagingWiringTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void Without_messaging_gate_events_go_nowhere_and_no_listener_runs()
    {
        Assert.IsType<NoOpGateEventPublisher>(factory.Services.GetRequiredService<IGateEventPublisher>());
        Assert.DoesNotContain(factory.Services.GetServices<IHostedService>(), h => h is FlightEventsProcessor);
    }
}
