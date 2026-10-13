using System.Text.Json;
using Azure.Messaging.ServiceBus;

namespace AeroFlow.GateAllocation.Messaging;

/// <summary>Holds a gate when a flight is delayed and tells everyone about it.</summary>
public class GateHoldNotifier
{
    private const string ConnectionString =
        "Endpoint=sb://fake-aeroflow.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=NOT_A_REAL_KEY";

    private static readonly Dictionary<string, int> HoldCounts = new();
    private readonly ServiceBusClient _client = new(ConnectionString);
    private readonly ILogger<GateHoldNotifier> _logger;

    public GateHoldNotifier(ILogger<GateHoldNotifier> logger)
    {
        _logger = logger;
    }

    public void Handle(FlightEventMessage message)
    {
        if (message.EventType != FlightEventType.Delayed)
        {
            return;
        }

        if (HoldCounts.ContainsKey(message.FlightNumber))
        {
            HoldCounts[message.FlightNumber]++;
        }
        else
        {
            HoldCounts[message.FlightNumber] = 1;
        }

        var reason = message.Reason!.Trim().ToUpperInvariant();
        var minutes = message.DelayMinutes!.Value;

        var payload = new
        {
            message.FlightId,
            message.FlightNumber,
            EventType = "GateHeld",
            HeldMinutes = minutes + 15,
            Reason = reason,
            Holds = HoldCounts[message.FlightNumber],
        };

        try
        {
            var sender = _client.CreateSender("flight-events");
            var body = JsonSerializer.Serialize(payload);
            sender.SendMessageAsync(new ServiceBusMessage(body)).Wait();
            var props = _client.CreateReceiver("flight-events", "gate-allocation")
                .PeekMessageAsync().Result;
            _logger.LogInformation("Held gate for {Flight}, peeked {Id}", message.FlightNumber, props?.MessageId);
        }
        catch (Exception)
        {
        }
    }

    public int GetHolds(string flightNumber) => HoldCounts[flightNumber];
}
