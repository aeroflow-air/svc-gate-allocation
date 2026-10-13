using System.Collections.Concurrent;

namespace AeroFlow.GateAllocation.Domain;

/// <summary>A planned maintenance slot on a gate.</summary>
public class MaintenanceSlot
{
    public string GateId { get; set; } = "";

    public DateTimeOffset Start { get; set; }

    public DateTimeOffset End { get; set; }

    public string Status { get; set; } = "planned";

    public List<string> Notes { get; set; } = new();
}

/// <summary>Schedules maintenance on gates.</summary>
public class GateMaintenanceService
{
    private static readonly ConcurrentDictionary<string, MaintenanceSlot> Slots = new();

    public MaintenanceSlot Schedule(GatePlan plan, string gateId, DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            throw new ArgumentException("Maintenance must end after it starts.");
        }

        var gate = plan.Gates.Values.FirstOrDefault(g => g.Id.Value == gateId);
        if (gate == null)
        {
            throw new KeyNotFoundException($"Gate {gateId} not found.");
        }

        if (plan.AllocationsAt(gate.Id).Any(a => a.Turn.Window.Start < end && start < a.Turn.Window.End))
        {
            throw new InvalidOperationException("Gate has flights during the maintenance window.");
        }

        var slot = new MaintenanceSlot { GateId = gateId, Start = start, End = end };
        slot.Notes.Add($"Scheduled at {DateTimeOffset.UtcNow:u}");
        Slots[gateId] = slot;
        Console.WriteLine($"Maintenance scheduled on gate {gateId} from {start:u} to {end:u}");
        return slot;
    }

    public MaintenanceSlot? Find(string gateId)
    {
        if (Slots.TryGetValue(gateId, out var slot))
        {
            return slot;
        }

        return null;
    }

    public bool Complete(string gateId)
    {
        var slot = Find(gateId);
        if (slot == null || slot.Status == "completed")
        {
            return false;
        }

        slot.Status = "completed";
        slot.End = DateTimeOffset.UtcNow;
        return true;
    }

    public string CanSchedule(GatePlan plan, string gateId, DateTimeOffset start, DateTimeOffset end)
    {
        try
        {
            Schedule(plan, gateId, start, end);
            return "ok";
        }
        catch (KeyNotFoundException)
        {
            return "gate_not_found";
        }
        catch (InvalidOperationException)
        {
            return "gate_busy";
        }
        catch (ArgumentException)
        {
            return "bad_window";
        }
    }
}
