// Fallacy 5: Topology doesn't change.
// Run from the entry folder: dotnet run dotnet/TopologyDoesntChange.cs   (.NET 10, no dependencies)
//
// At turn 1, the orchestrator plans the hotel step of trip-42 with the Hotel agent's tools. Before the step
// runs, the hotels' MCP server is redeployed: HoldHotel and ConfirmHotel replace BookHotel.

Console.WriteLine("Fallacy 5: Topology doesn't change");
Console.WriteLine();
Naive();
Console.WriteLine();
Fixed();

// Naive: plan once, at turn 1, and run the plan as written.
void Naive()
{
    Console.WriteLine("Naive: run the plan from turn 1 as written");
    var hotels = new HotelsServer();
    var hotelAgent = new HotelAgent(hotels);

    var orchestrator = new Orchestrator(hotelAgent, onToolsChanged: ToolsChanged.KeepPlan);   // (1)
    var step = orchestrator.Plan("at turn 1");                                                // (2)
    hotels.Redeploy(HotelsServer.HoldThenConfirm, notify: hotelAgent);
    orchestrator.Run(step);                                                                   // (3)

    PrintResult(hotels);
}

// Fixed: before each step runs, the runtime checks its tools against the agent's current ones.
void Fixed()
{
    Console.WriteLine("Fixed: before each step, check its tools against the agent's current ones");
    var hotels = new HotelsServer();
    var hotelAgent = new HotelAgent(hotels);

    var orchestrator = new Orchestrator(hotelAgent, onToolsChanged: ToolsChanged.PlanAgain);
    try
    {
        var step = orchestrator.Plan("at turn 1");   // the step keeps full tool definitions
        hotels.Redeploy(HotelsServer.HoldThenConfirm, notify: hotelAgent);
        orchestrator.Run(step);
        PrintResult(hotels);
    }
    catch (NoPlanException)
    {
        Console.WriteLine("  Result: no room booked yet; tell the traveler");
    }
}

void PrintResult(HotelsServer hotels)
{
    var lapses = string.Concat(hotels.Holds.Keys.Select(hold => $"; hold {hold} lapses in 15 minutes"));
    Console.WriteLine(hotels.Bookings.Count > 0 ? $"  Result: 1 room booked for trip-42: {hotels.Bookings[0]}"
        : $"  Result: no room booked for trip-42{lapses}");
}

// ----- The simulated world ------------------------------------------------------------------

// A tool definition as the server lists it. Records compare by value, so a tool changed in place counts as changed.
record Tool(string Name, string Takes, string Returns);

// One step of the plan, with the full tool definitions it was planned with, in the order to call them.
record Step(string Id, string Stay, Tool[] Tools);

// A tool call as data: what the model wants done.
record ToolCall(string Tool, string Argument);

enum ToolsChanged { KeepPlan, PlanAgain }

sealed class NoPlanException(string message) : Exception(message);

// Stands in for the orchestrator: a model that plans a step as a chain of the agent's tools, in list order,
// from the stay toward a booking, and a runtime that runs each step (with PlanAgain, only if complete and current).
sealed class Orchestrator(HotelAgent hotelAgent, ToolsChanged onToolsChanged)
{
    private const string Stay = "Garden Inn, 2 nights";

    public Step Plan(string when)
    {
        Console.WriteLine($"  orchestrator plans {when}, with the Hotel agent's tools: {string.Join(", ", hotelAgent.Tools.Select(t => t.Name))}");
        List<Tool> chain = [];
        var have = "stay";
        foreach (var tool in hotelAgent.Tools)
        {
            if (have != "booking" && tool.Takes == have)
            {
                chain.Add(tool);
                have = tool.Returns;
            }
        }

        if (have != "booking" && onToolsChanged == ToolsChanged.PlanAgain)
        {
            Console.WriteLine("    orchestrator runtime: no plan for step-2 with the Hotel agent's tools; nothing called");
            throw new NoPlanException("step-2");
        }

        var calls = chain.Select((t, i) => $"{t.Name}({(i == 0 ? Stay : chain[i - 1].Returns)})");
        Console.WriteLine($"    step-2: Hotel agent, {string.Join(", then ", calls)}");
        return new Step("step-2", Stay, [.. chain]);
    }

    public void Run(Step step)
    {
        var gone = step.Tools.FirstOrDefault(t => !hotelAgent.Tools.Contains(t));
        if (onToolsChanged == ToolsChanged.PlanAgain && gone is not null)
        {
            Console.WriteLine($"    orchestrator runtime: {step.Id} was planned with {gone.Name}, which is gone or changed; plan again");
            step = Plan("again");
        }

        hotelAgent.Run(step);
    }
}

// Stands in for the Hotel agent: a runtime that reloads its tools when told and runs each call, and a model that
// calls its step's tools in order (for a missing one, the one that takes the same input) and reports it done.
sealed class HotelAgent(HotelsServer hotels)
{
    public Tool[] Tools { get; private set; } = hotels.Tools;

    public void ReloadTools() => Console.WriteLine(
        $"    Hotel agent runtime: reloaded its tools: {string.Join(", ", (Tools = hotels.Tools).Select(t => t.Name))}");

    public void Run(Step step)
    {
        string? result = step.Stay;
        foreach (var planned in step.Tools)
        {
            var tool = Tools.FirstOrDefault(t => t.Name == planned.Name) ?? Tools.FirstOrDefault(t => t.Takes == planned.Takes);
            var call = new ToolCall(tool?.Name ?? planned.Name, result);   // the model's choice, as data
            var swap = call.Tool == planned.Name ? "" : $"has no {planned.Name}; ";
            Console.WriteLine($"  Hotel agent {swap}asks for {call.Tool}({call.Argument})");
            if ((result = hotels.Call(call)) is null)
            {
                Console.WriteLine($"  Hotel agent: {step.Id} failed");
                return;
            }
        }

        Console.WriteLine($"  Hotel agent: {step.Id} done");
    }
}

// The hotels' MCP server: it lists its tools, sends tools/list_changed when they change, and refuses a call
// to a tool it doesn't list, with MCP's "Unknown tool" error.
sealed class HotelsServer
{
    private static readonly Tool Search = new("SearchHotels", "city", "hotels");
    private static readonly Tool Cancel = new("CancelHotel", "booking", "refund");
    public static readonly Tool[] HoldThenConfirm = [Search, new("HoldHotel", "stay", "hold"), new("ConfirmHotel", "hold", "booking"), Cancel];

    public Tool[] Tools { get; private set; } = [Search, new("BookHotel", "stay", "booking"), Cancel];

    public Dictionary<string, string> Holds { get; } = [];

    public List<string> Bookings { get; } = [];

    public void Redeploy(Tool[] tools, HotelAgent notify)
    {
        Console.WriteLine($"  hotels server: removes {string.Join(" and ", Tools.Except(tools).Select(t => t.Name))},"
            + $" adds {string.Join(" and ", tools.Except(Tools).Select(t => t.Name))}; sends tools/list_changed");
        Tools = tools;
        notify.ReloadTools();
    }

    public string? Call(ToolCall call)
    {
        var tool = Tools.FirstOrDefault(t => t.Name == call.Tool);
        var stay = tool?.Takes == "hold" ? Holds.GetValueOrDefault(call.Argument) : call.Argument;
        if (tool is null || stay is null)
        {
            Console.WriteLine($"    hotels server: {(tool is null ? $"Unknown tool: {call.Tool} (MCP error -32602)" : $"no hold {call.Argument}")}");
            return null;
        }

        if (tool.Returns == "hold")
        {
            var hold = $"H-{Holds.Count + 1:000}";
            Holds[hold] = stay;
            Console.WriteLine($"    hotels server: {hold} holds a room ({stay}) for 15 minutes");
            return hold;
        }

        Holds.Remove(call.Argument);
        var booking = $"B-{Bookings.Count + 1:000}";
        Bookings.Add($"{stay} ({booking})");
        Console.WriteLine($"    hotels server: booked {booking} ({stay})");
        return booking;
    }
}
