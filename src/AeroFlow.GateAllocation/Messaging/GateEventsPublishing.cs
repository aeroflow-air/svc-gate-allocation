using AeroFlow.GateAllocation.Domain;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace AeroFlow.GateAllocation.Messaging;

/// <summary>Binds the <c>GateEvents</c> section.</summary>
public sealed class GateEventsOptions
{
    public const string SectionName = "GateEvents";

    public string Topic { get; set; } = "gate-events";
}

/// <summary>Sends gate events. Service Bus when <c>messaging</c> exists; otherwise a no-op.</summary>
public interface IGateEventPublisher
{
    Task PublishAsync(IReadOnlyCollection<GateEvent> events, CancellationToken cancellationToken);
}

/// <summary>Used when no <c>messaging</c> connection is configured: nothing leaves the process.</summary>
public sealed class NoOpGateEventPublisher : IGateEventPublisher
{
    public Task PublishAsync(IReadOnlyCollection<GateEvent> events, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Publishes to <c>gate-events</c>: JSON body, Subject = event type, CorrelationId = flight id, MessageId = event id.</summary>
public sealed partial class ServiceBusGateEventPublisher(
    ServiceBusClient client,
    IOptions<GateEventsOptions> options,
    ILogger<ServiceBusGateEventPublisher> logger) : IGateEventPublisher, IAsyncDisposable
{
    private readonly ILogger _logger = logger;
    private readonly ServiceBusSender _sender = client.CreateSender(options.Value.Topic);

    public static ServiceBusMessage ToMessage(GateEvent evt) =>
        new(BinaryData.FromObjectAsJson(evt, GateEvent.JsonOptions))
        {
            MessageId = evt.EventId.ToString(),
            Subject = evt.EventType.ToString(),
            CorrelationId = evt.FlightId,
            ContentType = "application/json",
        };

    public async Task PublishAsync(IReadOnlyCollection<GateEvent> events, CancellationToken cancellationToken)
    {
        foreach (var evt in events)
        {
            await _sender.SendMessageAsync(ToMessage(evt), cancellationToken);
            LogPublished(evt.EventType, evt.FlightNumber, evt.Gate);
        }
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();

    [LoggerMessage(Level = LogLevel.Information, Message = "Published {EventType} for {FlightNumber} at gate {Gate}")]
    private partial void LogPublished(GateEventType eventType, string flightNumber, string gate);
}
