// Fallacy 6: There is one administrator.
// Run from the entry folder: dotnet run dotnet/OneAdministrator.cs   (.NET 10, no dependencies)
//
// The example system with one change: the Research agent is connected to the payment gateway as well
// as the web. The Payment agent pays the Garden Inn; then the Research agent checks its cancellation policy.

const string PayTheGardenInn = "pay 360 EUR for trip-42 to GARDEN-INN-001";
const string CheckThePolicy = "read the Garden Inn's cancellation policy: can CH-001 be refunded?";
Dictionary<string, string[]> grantsByTheGatewaysAdministrator = new()
{
    ["payment-agent"] = ["Charge", "Refund"],
};

Console.WriteLine("Fallacy 6: There is one administrator");
Console.WriteLine();
Naive();
Console.WriteLine();
Fixed();

// Naive: the runtime signs every agent's gateway calls with the one key in the team's config.
void Naive()
{
    Console.WriteLine("Naive: sign every agent's gateway calls with the one key in the team's config");
    var gateway = new PaymentGateway(grantsByTheGatewaysAdministrator);
    var host = new AgentHost(gateway, credentials: Credentials.SharedKey);                  // (1)
    var paymentAgent = new PaymentAgent(tools: PaymentGateway.Tools);
    var researchAgent = new ResearchAgent(tools: ["FetchPage", .. PaymentGateway.Tools]);   // (2)

    host.Run(paymentAgent, PayTheGardenInn);
    host.Run(researchAgent, CheckThePolicy);                                                // (3)

    PrintResult(gateway);
}

// Fixed: the runtime asks the gateway for each agent's own key, one tool at a time.
void Fixed()
{
    Console.WriteLine("Fixed: ask the gateway for each agent's own key, one tool at a time");
    var gateway = new PaymentGateway(grantsByTheGatewaysAdministrator);
    var host = new AgentHost(gateway, credentials: Credentials.KeyPerAgent);   // never one key for all
    var paymentAgent = new PaymentAgent(tools: PaymentGateway.Tools);
    var researchAgent = new ResearchAgent(tools: ["FetchPage", .. PaymentGateway.Tools]);

    if (!host.Run(paymentAgent, PayTheGardenInn))
    {
        Console.WriteLine("  Result: nothing paid for trip-42;"
            + " only the gateway's administrator can grant Charge");
        return;
    }

    host.Run(researchAgent, CheckThePolicy);
    PrintResult(gateway);
}

void PrintResult(PaymentGateway gateway)
{
    Console.WriteLine($"  payment gateway log: {string.Join("; ", gateway.Log)}");
    var refund = gateway.Refunds.GetValueOrDefault("CH-001");
    var refused = gateway.Log.FirstOrDefault(entry => entry.StartsWith("Refund refused"))?.Split(' ')[^1];
    var result = refund == gateway.SecretKey
        ? $"CH-001 refunded, 360 EUR, and the gateway's log names {refund.Holder}, not an agent"
        : refund is not null ? $"CH-001 refunded, 360 EUR, by {refund.Holder}, as the gateway's log shows"
        : refused is not null ? $"CH-001 stays paid; the gateway refused {refused} and logged it"
        : "CH-001 stays paid";
    Console.WriteLine($"  Result: {result}");
}

// ----- The simulated world ------------------------------------------------------------------

// A tool call as data: what the model wants done.
record ToolCall(string Tool, string Argument);

// A gateway key: who holds it, as the gateway records it, and the tools it can call.
record Key(string Id, string Holder, string[] Tools);

enum Credentials { SharedKey, KeyPerAgent }

// An agent as its runtime sees it: an identity, a display name, the tools it's connected to, and a model.
abstract class Agent(string id, string name, string[] tools)
{
    public string Id => id;
    public string Name => name;
    public string[] Tools => tools;
    public abstract IEnumerable<ToolCall> Decide(string task);
}

// Stands in for the Payment agent's model: it asks to charge the amount and the account its task names.
sealed class PaymentAgent(string[] tools) : Agent("payment-agent", "Payment agent", tools)
{
    public override IEnumerable<ToolCall> Decide(string task) =>
        [new("Charge", $"{task.Split(' ')[1]} EUR, {task.Split(' ')[^1]}")];
}

// Stands in for the Research agent's model: it reads the page its task names, then calls every other tool on
// its list whose name the task contains, on the charge the task names.
sealed class ResearchAgent(string[] tools) : Agent("research-agent", "Research agent", tools)
{
    public override IEnumerable<ToolCall> Decide(string task) =>
        [new("FetchPage", "Garden Inn cancellation policy"),
         .. Tools.Where(tool => tool != "FetchPage" && task.Contains(tool, StringComparison.OrdinalIgnoreCase))
                 .Select(tool => new ToolCall(tool, task.Split(' ')[^3]))];
}

// Stands in for the agents' runtime, which runs each tool call a model asks for. With SharedKey, it signs
// gateway calls with the key in the team's config; with KeyPerAgent, it asks the gateway for a key for the
// agent it's running and this one tool, and calls nothing if the gateway refuses. The model never sees a key.
sealed class AgentHost(PaymentGateway gateway, Credentials credentials)
{
    public bool Run(Agent agent, string task)
    {
        foreach (var call in agent.Decide(task))
        {
            Console.WriteLine($"  {agent.Name} asks for {call.Tool}({call.Argument})");
            if (call.Tool == "FetchPage")
            {
                Console.WriteLine("    web: \"Free cancellation up to 48 hours before arrival.\"");
                continue;
            }

            var key = credentials == Credentials.SharedKey ? gateway.SecretKey : null;
            Console.WriteLine(key is null
                ? $"    runtime: asks the payment gateway for a {agent.Id} key for {call.Tool}"
                : $"    runtime: signs with {key.Id}, {key.Holder}'s key for every tool");
            key ??= gateway.Issue(agent.Id, call.Tool);
            if (key is null || !gateway.Call(key, call))
            {
                return false;
            }
        }

        return true;
    }
}

// The payment gateway behind Charge and Refund. Its administrator, not the travel team, grants each agent its
// tools. The account's secret key came with the account: every tool, no expiry, and no grant applies to it.
sealed class PaymentGateway(Dictionary<string, string[]> grants)
{
    public static readonly string[] Tools = ["Charge", "Refund"];
    public Key SecretKey { get; } = new("KEY-001", "the account owner", Tools);
    public List<string> Log { get; } = [];
    public Dictionary<string, Key> Refunds { get; } = [];
    private readonly Dictionary<string, string> _charges = [];
    private int _keys = 1;

    public Key? Issue(string agent, string tool)
    {
        if (!grants.GetValueOrDefault(agent, []).Contains(tool))
        {
            Console.WriteLine($"    payment gateway: refused; its administrator grants {agent} no {tool}");
            Log.Add($"{tool} refused to {agent}");
            return null;
        }

        var key = new Key($"KEY-{++_keys:000}", agent, [tool]);
        Console.WriteLine($"    payment gateway: issued {key.Id} to {agent}, for {tool} only");
        return key;
    }

    public bool Call(Key key, ToolCall call)
    {
        var charge = call.Tool == "Charge" ? $"CH-{_charges.Count + 1:000}" : call.Argument;
        var refundable = _charges.ContainsKey(charge) && !Refunds.ContainsKey(charge);
        if (!key.Tools.Contains(call.Tool) || (call.Tool == "Refund" && !refundable))
        {
            Console.WriteLine($"    payment gateway: refused {call.Tool}({call.Argument})");
            return false;
        }

        if (call.Tool == "Charge")
        {
            _charges[charge] = call.Argument.Split(", ")[0];
            Console.WriteLine($"    payment gateway: paid {_charges[charge]} to {call.Argument.Split(", ")[1]} ({charge})");
            Log.Add($"{charge} charged by {key.Holder}");
            return true;
        }

        Refunds[charge] = key;
        Console.WriteLine($"    payment gateway: refunded {_charges[charge]} on {charge}");
        Log.Add($"{charge} refunded by {key.Holder}");
        return true;
    }
}
