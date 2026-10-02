// Fallacy 2: Latency is zero.
// Run from the entry folder: dotnet run dotnet/LatencyIsZero.cs   (.NET 10, no dependencies)
//
// To plan a trip, the orchestrator needs a flight, a hotel, the hotel's reviews, and the visa rules.
// The user's app gives up after 20 seconds. Time is simulated, so the program runs instantly.

const int UserDeadline = 20;   // seconds

Console.WriteLine("Fallacy 2: Latency is zero");
Console.WriteLine();
Naive();
Console.WriteLine();
Fixed();

// Naive: one agent at a time, one small question at a time, and the deadline stays with the user.
void Naive()
{
    Console.WriteLine("Naive: one agent at a time, one small question at a time");
    var timeline = new Timeline(UserDeadline);
    var flightAgent = new Agent("Flight agent", SecondsPerTurn: 4);
    var hotelAgent = new Agent("Hotel agent", SecondsPerTurn: 4);
    var researchAgent = new Agent("Research agent", SecondsPerTurn: 6);

    var now = 0;
    now = timeline.Ask(flightAgent, "search flights to London", startAt: now);      // (1)
    now = timeline.Ask(flightAgent, "pick the morning flight", startAt: now);       // (2)
    now = timeline.Ask(hotelAgent, "search hotels near the office", startAt: now);
    now = timeline.Ask(hotelAgent, "pick the one with breakfast", startAt: now);
    now = timeline.Ask(researchAgent, "check the hotel's reviews", startAt: now);   // (3)
    now = timeline.Ask(researchAgent, "check the visa rules", startAt: now);

    PrintResult(planReadyAt: now, timeline);
}

// Fixed: one message per task, independent tasks at the same time, and the deadline passed down.
void Fixed()
{
    Console.WriteLine("Fixed: one message per task, in parallel when independent, deadline passed down");
    var timeline = new Timeline(UserDeadline);
    var flightAgent = new Agent("Flight agent", SecondsPerTurn: 4);
    var hotelAgent = new Agent("Hotel agent", SecondsPerTurn: 4);
    var researchAgent = new Agent("Research agent", SecondsPerTurn: 6);

    // The flight, the hotel, and the visa rules don't depend on each other: ask for them together.
    var flight = timeline.Ask(flightAgent, "find the morning flight to London",
        startAt: 0, deadline: UserDeadline);
    var hotel = timeline.Ask(hotelAgent, "find a hotel near the office, with breakfast",
        startAt: 0, deadline: UserDeadline);
    var visa = timeline.Ask(researchAgent, "check the visa rules",
        startAt: 0, deadline: UserDeadline);
    // The reviews need the hotel, so they start when it's chosen.
    var reviews = timeline.Ask(researchAgent, "check the hotel's reviews",
        startAt: hotel, deadline: UserDeadline);

    PrintResult(planReadyAt: new[] { flight, hotel, visa, reviews }.Max(), timeline);
}

void PrintResult(int planReadyAt, Timeline timeline)
{
    if (timeline.Canceled.Count > 0)
    {
        Console.WriteLine($"  Result: partial plan at {UserDeadline} s; missing: {string.Join("; ", timeline.Canceled)}");
    }
    else if (planReadyAt <= UserDeadline)
    {
        Console.WriteLine($"  Result: plan ready at {planReadyAt} s, within the {UserDeadline} s deadline");
    }
    else
    {
        Console.WriteLine($"  Result: plan ready at {planReadyAt} s, but the user gave up at {UserDeadline} s;"
            + $" {timeline.TurnsAfterUserLeft} agent turns ended after that");
    }
}

// ----- The simulated world ------------------------------------------------------------------

// An agent, reduced to what matters here: a model turn takes seconds, not milliseconds.
record Agent(string Name, int SecondsPerTurn);

// Stands in for the agents' models and the clock. Nothing waits: Ask puts a turn on the timeline and
// returns the second it ends, which is when anything that needs its answer can start.
sealed class Timeline(int userDeadline)
{
    public int TurnsAfterUserLeft { get; private set; }

    public List<string> Canceled { get; } = [];

    public int Ask(Agent agent, string task, int startAt, int? deadline = null)
    {
        var endAt = startAt + agent.SecondsPerTurn;
        if (deadline is not null && endAt > deadline)
        {
            Canceled.Add(task);
            Console.WriteLine($"  {startAt,2} s -> {deadline,2} s  {agent.Name}: {task}   <- canceled at the deadline");
            return deadline.Value;
        }

        var afterUserLeft = endAt > userDeadline;
        if (afterUserLeft)
        {
            TurnsAfterUserLeft++;
        }

        var note = afterUserLeft ? "   <- the user has gone" : "";
        Console.WriteLine($"  {startAt,2} s -> {endAt,2} s  {agent.Name}: {task}{note}");
        return endAt;
    }
}
