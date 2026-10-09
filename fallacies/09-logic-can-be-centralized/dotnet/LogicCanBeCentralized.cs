// Fallacy 9: Business logic can and should be centralized.
// Run from the entry folder: dotnet run dotnet/LogicCanBeCentralized.cs   (.NET 10, no dependencies)
//
// The example system with one change: the agents run as a handoff workflow, so the Hotel agent can hand
// off to the Payment agent. trip-42 books 6 rooms; payments over 2,000 EUR need the travel manager's approval.

using System.Globalization;
using System.Text.RegularExpressions;

const string BookTheRooms = "Book 6 rooms at the Garden Inn for 2 nights from March 1.";
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;   // "2,160 EUR" on every machine

Console.WriteLine("Fallacy 9: Business logic can and should be centralized");
Console.WriteLine();
Naive();
Console.WriteLine();
Fixed();

// Naive: the runtime writes the rule only into the orchestrator's instructions, and Charge pays any amount.
void Naive()
{
    Console.WriteLine("Naive: write the approval rule in the orchestrator's instructions, and let the agents hand off");
    var (hotels, gateway) = (new Hotels(), new PaymentGateway());
    var rule = new ApprovalRule(OverEur: 2_000, Approver: "the travel manager");
    var runtime = new AgentRuntime(hotels, gateway, rule, ruleIn: Place.Instructions);   // (1)
    var hotelAgent = new HotelAgent(Handoffs: ["orchestrator", "Payment agent"]);        // (2)
    var paymentAgent = new PaymentAgent();                                               // (3)

    runtime.Run(BookTheRooms, new Orchestrator(), hotelAgent, paymentAgent);
    PrintResult(runtime, gateway, rule);
}

// Fixed: Charge runs the rule for every caller, and the instructions are written from the same rule.
void Fixed()
{
    Console.WriteLine("Fixed: run the approval rule in Charge, and write the instructions from it");
    var (hotels, gateway) = (new Hotels(), new PaymentGateway());
    var rule = new ApprovalRule(OverEur: 2_000, Approver: "the travel manager");
    var runtime = new AgentRuntime(hotels, gateway, rule, ruleIn: Place.Charge);   // where money moves
    var hotelAgent = new HotelAgent(Handoffs: ["orchestrator", "Payment agent"]);
    var paymentAgent = new PaymentAgent();

    if (!runtime.Run(BookTheRooms, new Orchestrator(), hotelAgent, paymentAgent))
    {
        Console.WriteLine("  Result: B-001 booked, nothing paid;"
            + $" no one is set to approve a payment over {rule.OverEur:N0} EUR");
        return;
    }

    PrintResult(runtime, gateway, rule);
}

void PrintResult(AgentRuntime runtime, PaymentGateway gateway, ApprovalRule rule) =>
    Console.WriteLine("  Result: " + (runtime.Held, gateway.Paid) switch
    {
        ({ } held, _) => $"B-001 booked; {held:N0} EUR waits for {rule.Approver}'s approval (AR-001), nothing paid yet",
        (_, { } paid) => $"{paid:N0} EUR paid for B-001 (CH-001), " + (paid > rule.OverEur
            ? "and no one was asked to approve it" : $"not over {rule.OverEur:N0} EUR, so no approval needed"),
        _ => "B-001 booked, nothing paid; no approval request was filed",
    });

// ----- The simulated world ------------------------------------------------------------------

// What a model wants next, as data: a tool call, a handoff, or a reply. The runtime carries it out.
record Turn(string? Tool = null, string? Argument = null, string? HandOffTo = null, string? Say = null);

// Where the runtime puts the approval rule: in the orchestrator's instructions, or in Charge.
enum Place { Instructions, Charge }

// The travel policy's rule, defined once; the orchestrator's instructions are written from it either way.
record ApprovalRule(int OverEur, string? Approver)
{
    public string AsRule => $"Payments over {OverEur:N0} EUR need {Approver ?? "someone"}'s approval.";
    public string AsFact => Approver is null ? $"Charge refuses payments over {OverEur:N0} EUR."
        : $"Charge holds payments over {OverEur:N0} EUR for {Approver}'s approval.";

    // How the models and Charge read an amount in text: "2,160 EUR" is 2160.
    public static int? Eur(string text) => Regex.Match(text, @"([\d,]+) EUR") is { Success: true } match
        ? int.Parse(match.Groups[1].Value.Replace(",", "")) : null;
}

// An agent in the workflow: the runtime calls its model with its instructions and the conversation so far.
interface IAgent { string Name { get; } Turn Take(string instructions, List<string> conversation); }

// The orchestrator's model (no tools): it hands each task on, but stops a payment its instructions say needs approval, unless they leave it to Charge.
sealed record Orchestrator(string Name = "orchestrator") : IAgent
{
    public Turn Take(string instructions, List<string> conversation) => ApprovalRule.Eur(conversation[^1]) switch
    {
        null => new(HandOffTo: "Hotel agent", Say: conversation[^1]),
        var amount when !instructions.StartsWith("Charge") && amount > ApprovalRule.Eur(instructions)
            => new(Say: $"B-001 costs {amount:N0} EUR. {instructions}"),
        _ => new(HandOffTo: "Payment agent", Say: conversation[^1]),
    };
}

// The Hotel agent's model: it books the rooms its task names, then hands the payment to the Payment agent if it may, or back to the orchestrator.
sealed record HotelAgent(string[] Handoffs, string Name = "Hotel agent") : IAgent
{
    public Turn Take(string instructions, List<string> conversation) => conversation[^1].StartsWith("booked")
        ? new(HandOffTo: Handoffs.Contains("Payment agent") ? "Payment agent" : "orchestrator",
            Say: $"B-001 is booked. Pay {ApprovalRule.Eur(conversation[^1]):N0} EUR to {conversation[^1].Split(' ')[^1]}.")
        : new(Tool: "BookHotel", Argument: $"Garden Inn, 2027-03-01, 2 nights, {string.Join(' ', conversation[^1].Split(' ')[1..3])}");
}

// The Payment agent's model: it pays what its last message names, and tells the traveler; no approval rule in its instructions.
sealed record PaymentAgent(string Name = "Payment agent") : IAgent
{
    public Turn Take(string instructions, List<string> conversation) => conversation[^1].StartsWith("B-001 is")
        ? new(Tool: "Charge", Argument: $"{ApprovalRule.Eur(conversation[^1]):N0} EUR, {conversation[^1].Split(' ')[^1].TrimEnd('.')}")
        : new(Say: $"B-001: {conversation[^1]}.");
}

// The agents' runtime, as in a handoff workflow: it runs each turn's tool calls and passes control on each handoff.
sealed class AgentRuntime(Hotels hotels, PaymentGateway gateway, ApprovalRule rule, Place ruleIn)
{
    public int? Held { get; private set; }

    // Runs the request from the orchestrator; false when Charge refused a payment no one is set to approve.
    public bool Run(string request, IAgent orchestrator, params IAgent[] agents)
    {
        var instructions = ruleIn == Place.Instructions ? rule.AsRule : rule.AsFact;
        Console.WriteLine($"  runtime: instructs the orchestrator: \"{instructions}\"");
        var (current, conversation, turn) = (orchestrator, new List<string> { request }, new Turn());
        while ((turn = current.Take(current == orchestrator ? instructions : "", conversation)) is not { Tool: null, HandOffTo: null })
        {
            if (turn.Tool is { } tool)
            {
                Console.WriteLine($"  {current.Name} asks for {tool}({turn.Argument})");
                if ((tool == "BookHotel" ? hotels.Book(turn.Argument!) : Charge(turn.Argument!)) is not { } result) return false;
                conversation.Add(result);
                continue;
            }

            Console.WriteLine($"  {current.Name} hands off to the {turn.HandOffTo}: \"{turn.Say}\"");
            if (current != orchestrator && turn.HandOffTo != orchestrator.Name)
                Console.WriteLine($"    runtime: hands control to the {turn.HandOffTo}, past the orchestrator and its instructions, no error");
            current = turn.HandOffTo == orchestrator.Name ? orchestrator : agents.Single(agent => agent.Name == turn.HandOffTo);
            conversation.Add(turn.Say!);
        }

        Console.WriteLine($"  {current.Name} replies to the traveler: \"{turn.Say}\"");
        return true;
    }

    // Charge as the runtime runs it. With the rule here, a payment over the limit waits for its approver.
    string? Charge(string argument)
    {
        var amount = ApprovalRule.Eur(argument)!.Value;
        if (ruleIn == Place.Instructions || amount <= rule.OverEur)
            return gateway.Charge(amount, argument.Split(", ")[^1]);
        if (rule.Approver is null)   // no one can approve it: refuse, and never fall back to paying
        {
            Console.WriteLine($"    runtime: Charge refuses {amount:N0} EUR, over {rule.OverEur:N0} EUR: no one is set to approve it");
            return null;
        }

        Held = amount;
        Console.WriteLine($"    runtime: Charge holds {amount:N0} EUR, over {rule.OverEur:N0} EUR, for {rule.Approver}'s approval (AR-001)");
        return $"held for {rule.Approver}'s approval (AR-001)";   // a result the model reports, not an error it retries
    }
}

// The hotels behind BookHotel: 180 EUR a room a night at the Garden Inn, paid to its account on file.
sealed class Hotels
{
    public string Book(string argument)
    {
        var rooms = argument.Split(", ")[3];
        var total = 180 * 2 * int.Parse(rooms.Split(' ')[0]);
        Console.WriteLine($"    hotels: booked B-001 (Garden Inn, {rooms}, March 1-3, {total:N0} EUR)");
        return $"booked B-001 for {total:N0} EUR, payable to GARDEN-INN-001";
    }
}

// The payment gateway behind Charge: it pays whatever it's asked to.
sealed class PaymentGateway
{
    public int? Paid { get; private set; }

    public string Charge(int amount, string payeeAccount)
    {
        Paid = amount;
        Console.WriteLine($"    payment gateway: paid {amount:N0} EUR to {payeeAccount} (CH-001)");
        return $"paid {amount:N0} EUR to {payeeAccount} (CH-001)";
    }
}
