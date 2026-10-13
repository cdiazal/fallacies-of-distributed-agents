// Fallacy 10: The system is monolithic.
// Run from the entry folder: dotnet run dotnet/SystemIsMonolithic.cs   (.NET 10, no dependencies)
//
// The example system with one change: the orchestrator plans trip-42 once, and the runtime runs the plan
// step by step. trip-42 books FL101 and the Garden Inn, then pays the hotel; the company card is declined.

const string BookTheTrip = "Book me a trip from Madrid to London on March 1, 2027: a flight and two nights in a hotel"
    + " near the office. Pay with the company card.";

Console.WriteLine("Fallacy 10: The system is monolithic");
Console.WriteLine();
Naive();
Console.WriteLine();
Fixed();

// Naive: the runtime records nothing it could undo, and ends trip-42 on the last reply.
void Naive()
{
    Console.WriteLine("Naive: run the plan step by step, and end trip-42 on the last reply");
    var (airline, hotels) = (Provider.Airline(), Provider.Hotels());
    var gateway = new PaymentGateway(CardOverLimit: true);
    var runtime = new AgentRuntime(airline, hotels, gateway, saga: null);    // (1)
    var plan = new Orchestrator().Plan(BookTheTrip);                         // (2)

    var escalated = runtime.Run(plan, new BookingAgent("Flight agent", "BookFlight"),
        new BookingAgent("Hotel agent", "BookHotel"), new PaymentAgent());   // (3)
    PrintResult(airline, hotels, gateway, escalated);
}

// Fixed: the runtime records an undo as each booking succeeds, and runs them newest first if trip-42 ends unpaid.
void Fixed()
{
    Console.WriteLine("Fixed: record an undo for every booking, and run them newest first if trip-42 ends unpaid");
    var (airline, hotels) = (Provider.Airline(), Provider.Hotels());
    var gateway = new PaymentGateway(CardOverLimit: true);
    var saga = new Saga(Pivot: "Charge",
        Undo: new() { ["BookFlight"] = "CancelFlight", ["BookHotel"] = "CancelHotel" });
    var runtime = new AgentRuntime(airline, hotels, gateway, saga: saga);   // per tool, not per step
    var plan = new Orchestrator().Plan(BookTheTrip);

    var escalated = runtime.Run(plan, new BookingAgent("Flight agent", "BookFlight"),
        new BookingAgent("Hotel agent", "BookHotel"), new PaymentAgent());
    PrintResult(airline, hotels, gateway, escalated);
}

void PrintResult(Provider airline, Provider hotels, PaymentGateway gateway, List<string> escalated)
{
    var held = string.Join(" and ", new[] { airline, hotels }.Where(p => p.Held).Select(p => p.Id));
    Console.WriteLine("  Result: " + (gateway.Paid is { } paid ? $"{held} booked, and paid {paid}"
        : escalated.Count > 0 ? $"nothing paid; the travel manager has {string.Join(" and ", escalated)}"
        : held == "" ? "nothing booked or paid for trip-42" : $"trip-42 isn't paid, and nothing will cancel {held}"));
}

// ----- The simulated world ------------------------------------------------------------------

// What a model wants next, as data: a tool call or a reply. The runtime carries it out.
record Turn(string? Tool = null, string? Argument = null, string? Say = null);

// What a tool call returns: the runtime reads Ok and Id; the model reads only the text.
record ToolResult(bool Ok, string Text, string? Id = null);

// The run's point of no return, and the tool that undoes each other tool: per tool, so no plan has to list them.
record Saga(string Pivot, Dictionary<string, string> Undo);

// An agent: the runtime calls its model with its step's task and the conversation so far.
interface IAgent { string Name { get; } Turn Take(string task, List<string> conversation); }

// The orchestrator's model (no tools): a step each for the flight and the hotel, in the request's order, then one to pay.
sealed class Orchestrator
{
    public (string Agent, string Task)[] Plan(string request) => [.. new (int At, string Agent, string Task)[] {
        (request.IndexOf("flight"), "Flight agent", "Book FL101."),
        (request.IndexOf("hotel"), "Hotel agent", "Book Garden Inn, 2027-03-01, 2 nights.") }
        .OrderBy(b => b.At).Select(b => (b.Agent, b.Task)), ("Payment agent", "Pay the hotel.")];
}

// The Flight and Hotel agents' model: it asks its tool for what its task names, then replies with what came back.
sealed record BookingAgent(string Name, string Tool) : IAgent
{
    public Turn Take(string task, List<string> conversation) => conversation[^1] != task
        ? new(Say: conversation[^1]) : new(Tool: Tool, Argument: task["Book ".Length..].TrimEnd('.'));
}

// The Payment agent's model (Charge and Refund only): it pays what the hotel's reply names, then says how it went.
sealed record PaymentAgent(string Name = "Payment agent") : IAgent
{
    public Turn Take(string task, List<string> conversation)
    {
        var reply = conversation.Last(m => m.Contains("payable to")).TrimEnd('.').Split(' ');
        var paid = conversation[^1].Contains("declined") ? "isn't paid" : "is paid";
        return conversation[^1] != task ? new(Say: $"{reply[0]} {paid}: {conversation[^1]}.")
            : new(Tool: "Charge", Argument: $"{reply[3]} EUR, {reply[^1]}");
    }
}

// The agents' runtime: it runs each step's tool calls and ends the run on the last step or the first failed call;
// with a saga, it records an undo for each call that succeeds, and runs them newest first unless the pivot succeeded.
sealed class AgentRuntime(Provider airline, Provider hotels, PaymentGateway gateway, Saga? saga)
{
    readonly Dictionary<string, Func<string, ToolResult>> tools = new() { ["BookFlight"] = airline.Book,
        ["CancelFlight"] = airline.Cancel, ["BookHotel"] = hotels.Book, ["CancelHotel"] = hotels.Cancel, ["Charge"] = gateway.Charge };
    readonly Stack<(string Tool, string Booking, string Owner)> undos = new();

    // Runs the plan, and returns the bookings whose undo failed, for the travel manager; without a saga, it runs no undo.
    public List<string> Run((string Agent, string Task)[] plan, params IAgent[] agents)
    {
        Console.WriteLine("  orchestrator plans trip-42: " + string.Join(", ", plan.Select((s, i) => $"step-{i + 1} {s.Agent}")));
        var (conversation, pivoted, result, name) = (new List<string>(), false, new ToolResult(true, ""), "");
        foreach (var (step, (agentName, task)) in plan.Index())
        {
            var (agent, turn) = (agents.Single(a => a.Name == agentName), new Turn());
            name = agentName;
            conversation.Add(task);
            while ((turn = agent.Take(task, conversation)).Tool is { } tool)
            {
                Console.WriteLine($"  {name} asks for {tool}({turn.Argument})");
                conversation.Add((result = tools[tool](turn.Argument!)).Text);
                pivoted |= result.Ok && tool == saga?.Pivot;
                if (!result.Ok || saga?.Undo.GetValueOrDefault(tool) is not { } undoTool) continue;
                undos.Push((undoTool, result.Id!, name));   // from the typed result, never from a reply
                Console.WriteLine($"    runtime: records {undoTool}({result.Id}) to undo it");
            }

            var last = step == plan.Length - 1 || !result.Ok;
            Console.WriteLine($"  {name} replies{(last ? " to the traveler" : "")}: \"{turn.Say}\"");
            conversation.Add(turn.Say!);
            if (last) break;
        }

        Console.WriteLine($"    runtime: ends trip-42 on the {name}'s reply" + (saga is null && !result.Ok ? ", no error" : ""));
        if (saga is null || pivoted) return [];

        var escalated = new List<string>();
        Console.WriteLine($"  runtime: the log has no successful {saga.Pivot}; runs trip-42's undos, newest first");
        while (undos.TryPop(out var call))
        {
            Console.WriteLine($"  runtime: runs {call.Tool}({call.Booking}) as the {call.Owner}");
            if (tools[call.Tool](call.Booking).Ok) continue;
            Console.WriteLine($"    runtime: {call.Booking} stays booked; the travel manager is told, and no model retries it");
            escalated.Add(call.Booking);
        }

        Console.WriteLine("  runtime: tells the traveler: \"Nothing is paid for trip-42"
            + (escalated.Count == 0 ? ", and nothing is booked.\"" : $"; the travel manager has {string.Join(" and ", escalated)}.\""));
        return escalated;
    }
}

// The airline or the hotels: each books one thing, and cancels it unless its terms say no.
sealed class Provider(string name, string booked, string reply, string? refusesCancel)
{
    public bool Held { get; private set; }
    public string Id { get; } = booked.Split(' ', ',')[0];

    public static Provider Airline(string? refusesCancel = null) => new("airline",
        "FL101, Madrid 07:10 to London 08:35, lands March 1, 2027", "FL101 is booked for March 1.", refusesCancel);
    public static Provider Hotels(string? refusesCancel = null) => new("hotels",
        "B-001 (Garden Inn, March 1-3, 360 EUR)", "B-001 is booked: 360 EUR, payable to GARDEN-INN-001.", refusesCancel);

    public ToolResult Book(string argument)
    {
        Held = true;
        Console.WriteLine($"    {name}: booked {booked}");
        return new(true, reply, Id);
    }

    public ToolResult Cancel(string booking)
    {
        Held = refusesCancel is not null;
        Console.WriteLine($"    {name}: " + (Held ? $"refused to cancel {booking}: {refusesCancel}" : $"canceled {booking}"));
        return new(!Held, Held ? $"refused: {refusesCancel}" : $"canceled {booking}");
    }
}

// The payment gateway behind Charge: it charges the company card, and the card's issuer declines it over its limit.
sealed record PaymentGateway(bool CardOverLimit)
{
    public string? Paid { get; private set; }

    public ToolResult Charge(string argument)
    {
        Paid = CardOverLimit ? null : $"{argument.Replace(", ", " to ")} (CH-001)";
        Console.WriteLine("    payment gateway: " + (Paid is null
            ? $"declined {argument.Split(", ")[0]}: the company card is over its monthly limit" : $"paid {Paid}"));
        return Paid is null ? new(false, "the company card was declined") : new(true, Paid, "CH-001");
    }
}
