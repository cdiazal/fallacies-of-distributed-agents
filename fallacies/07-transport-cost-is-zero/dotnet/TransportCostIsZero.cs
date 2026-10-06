// Fallacy 7: Transport cost is zero.
// Run from the entry folder: dotnet run dotnet/TransportCostIsZero.cs   (.NET 10, no dependencies)
//
// The Hotel agent proposes a hotel for trip-42, and the orchestrator reviews each proposal in a group chat.
// Every model call carries the whole conversation. Token counts and prices are simulated.

using System.Globalization;

const string Trip42 = "trip-42: 2 nights near the office, March 1-3, under 200 EUR a night, rated 4 or better";
const string TravelPolicy = "keep costs down: while you can see a cheaper hotel, ask for something cheaper";
const int TokenBudgetPerRun = 45_000;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;   // 900,250 on every machine

Console.WriteLine("Fallacy 7: Transport cost is zero");
Console.WriteLine();
Naive();
Console.WriteLine();
Fixed();

// Naive: the agents talk until the group chat's default limit of 40 agent turns.
void Naive()
{
    Console.WriteLine("Naive: let the agents talk until the group chat's default limit of 40 agent turns");
    var hotels = new Hotels();
    var chat = new GroupChat(hotels, tokenBudget: null);               // (1)
    var hotelAgent = new HotelAgent();
    var orchestrator = new Orchestrator(Instructions: TravelPolicy);   // (2)

    chat.Run(Trip42, hotelAgent, orchestrator);                        // (3)
    PrintResult(hotels, chat);
}

// Fixed: the runtime gives the run a token budget and checks it before every model call.
void Fixed()
{
    Console.WriteLine($"Fixed: give the run a token budget of {TokenBudgetPerRun:N0} and check it before every model call");
    var hotels = new Hotels();
    var chat = new GroupChat(hotels, tokenBudget: TokenBudgetPerRun);   // the run's, not a model's
    var hotelAgent = new HotelAgent();
    var orchestrator = new Orchestrator(Instructions: TravelPolicy);

    if (chat.Run(Trip42, hotelAgent, orchestrator) is null)
    {
        Console.WriteLine("  Result: no room booked for trip-42;"
            + " the run stopped before the Hotel agent proposed one");
        return;
    }

    PrintResult(hotels, chat);
}

void PrintResult(Hotels hotels, GroupChat chat)
{
    var what = hotels.Booked is { } booked ? $"{booked.Name} booked for {booked.Price * 2} EUR; choosing it billed"
        : "no room booked for trip-42; the chat billed";
    var cents = (chat.MicroUsd + 5_000) / 10_000;
    Console.WriteLine($"  Result: {what} {chat.Billed:N0} tokens, {cents / 100}.{cents % 100:00} USD");
}

// ----- The simulated world ------------------------------------------------------------------

// A hotel as SearchHotels returns it.
record Hotel(string Name, int Price, double Rating);

// A chat message and its simulated size; the traveler's limits, SearchHotels, and BookHotel ride on it as data.
record Message(string Sender, string Text, int Tokens, int Under = 0, double Rated = 0,
    Hotel[]? Hotels = null, Hotel? Proposal = null, bool Accepted = false);

// The Hotel agent's model: search under the last proposal's price, then propose the best-rated hotel allowed.
sealed class HotelAgent
{
    public Message Decide(List<Message> conversation)
    {
        var last = conversation.LastOrDefault(m => m.Proposal is not null)?.Proposal;
        var (under, rated) = (last?.Price ?? conversation[0].Under, conversation[0].Rated);
        if (conversation[^1].Hotels is not { } found)
            return new("Hotel agent", $"searches under {under} EUR", 50, Under: under);

        var best = found.Where(h => h.Rating >= rated).MaxBy(h => h.Rating);
        var text = best is not null ? $"proposes {best.Name}, {best.Price} EUR a night, rated {best.Rating}"
            : last is not null ? $"proposes {last.Name} again: none rated {rated} or better"
            : $"proposes nothing: none rated {rated} or better";
        return new("Hotel agent", text, 250, Proposal: best ?? last);
    }
}

// The orchestrator's model: while it can see a hotel cheaper than the proposal, ask for something cheaper.
sealed record Orchestrator(string Instructions)
{
    public Message Review(List<Message> conversation)
    {
        var proposal = conversation[^1].Proposal;
        var cheaper = conversation.SelectMany(m => m.Hotels ?? [])
            .Where(h => proposal is not null && h.Price < proposal.Price).MinBy(h => h.Price);
        var text = proposal is null ? "No proposal. Find a hotel."
            : cheaper is not null ? $"{cheaper.Name} is cheaper. Find something cheaper." : "Good. Book it.";
        return new("orchestrator", $"\"{text}\"", 150, Proposal: proposal, Accepted: text == "Good. Book it.");
    }
}

// The group chat's runtime: every model call sends the instructions, the tools, and the whole conversation.
sealed class GroupChat(Hotels hotels, int? tokenBudget)
{
    const int MaxAgentTurns = 40;   // the framework's default
    const int InstructionsAndTools = 2_000, MaxOutputTokens = 1_000, UsdPerMillionIn = 3, UsdPerMillionOut = 15;
    private string[] _lastRound = [], _held = [];
    private string _stop = "";
    private int _firstRepeat;
    public long Billed { get; private set; }
    public long MicroUsd { get; private set; }

    public Hotel? Run(string request, HotelAgent hotelAgent, Orchestrator orchestrator)
    {
        List<Message> conversation = [new("traveler", request, 100, Under: 200, Rated: 4)];
        var number = 0;
        while (!conversation[^1].Accepted && Round(++number, conversation, hotelAgent, orchestrator)) { }
        foreach (var line in _held.Append(_stop).Where(line => line != "")) Console.WriteLine(line);
        var proposal = conversation.LastOrDefault(m => m.Proposal is not null)?.Proposal;
        if (proposal is null) return null;
        Console.WriteLine($"  runtime: runs the latest proposal, BookHotel({proposal.Name}, 2 nights)");
        return hotels.Book(proposal);
    }

    // Two agent turns, the Hotel agent's (search, then propose) and the orchestrator's; repeats print as a range.
    private bool Round(int number, List<Message> conversation, HotelAgent hotelAgent, Orchestrator orchestrator)
    {
        var (start, before) = (conversation.Count, Billed);
        var finished = Call(conversation, 2 * number - 1, () => hotelAgent.Decide(conversation))
            && Call(conversation, 2 * number - 1, () => hotelAgent.Decide(conversation))
            && Call(conversation, 2 * number, () => orchestrator.Review(conversation));
        string[] said = [.. conversation.Skip(start).Where(m => m.Sender != "hotels").Select(m => m.Text)];
        var billed = $"    runtime: round {number} billed {Billed - before:N0} tokens; {Billed:N0} in all";
        if (said.Length == 0) return false;
        if (finished && said.SequenceEqual(_lastRound))
        {
            _firstRepeat = _firstRepeat == 0 ? number : _firstRepeat;
            var rounds = _firstRepeat == number ? $"round {number}" : $"rounds {_firstRepeat}-{number}";
            _held = [$"  {rounds}: the same search, proposal, and answer", billed];
            return true;
        }

        foreach (var line in _held) Console.WriteLine(line);
        Console.WriteLine($"  round {number}: Hotel agent {string.Join(", then ", said.Take(2))}");
        foreach (var text in said.Skip(2)) Console.WriteLine($"    orchestrator: {text}");
        Console.WriteLine(billed);
        (_held, _lastRound, _firstRepeat) = ([], said, 0);
        return finished;
    }

    private bool Call(List<Message> conversation, int turn, Func<Message> model)
    {
        var sending = InstructionsAndTools + conversation.Sum(m => m.Tokens);
        if (turn > MaxAgentTurns)
            _stop = $"    runtime: {MaxAgentTurns} agent turns, the chat's limit; ends the chat, no error";
        else if (tokenBudget is { } budget && Billed + sending + MaxOutputTokens > budget)
            _stop = $"    runtime: the next call could bill up to {sending + MaxOutputTokens:N0} tokens;"
                + $" the run has {budget - Billed:N0} left, so it ends the chat";
        if (_stop != "") return false;

        var reply = model();
        Billed += sending + reply.Tokens;
        MicroUsd += sending * UsdPerMillionIn + reply.Tokens * UsdPerMillionOut;
        conversation.Add(reply);
        if (reply.Under > 0) conversation.Add(hotels.Search(reply.Under));
        return true;
    }
}

// The hotels near the office, behind SearchHotels and BookHotel. A search result is 750 tokens a hotel.
sealed class Hotels
{
    private readonly Hotel[] _near = [new("Regent", 340, 4.8), new("Garden Inn", 180, 4.5), new("Station Hotel", 150, 3.9)];
    public Hotel? Booked { get; private set; }

    public Message Search(int under) =>
        new("hotels", "", 750 * _near.Count(h => h.Price < under), Hotels: [.. _near.Where(h => h.Price < under)]);

    public Hotel Book(Hotel hotel)
    {
        Console.WriteLine($"    hotels: booked B-001 ({hotel.Name}, 2 nights, {hotel.Price * 2} EUR)");
        return Booked = hotel;
    }
}
