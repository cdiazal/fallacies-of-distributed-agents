// Fallacy 3: Bandwidth is infinite.
// Run from the entry folder: dotnet run dotnet/BandwidthIsInfinite.cs   (.NET 10, no dependencies)
//
// The Research agent has read every review of the hotels near the office, and the orchestrator now asks
// the Hotel agent to book one. At the start of the run, the user set a budget: 200 EUR a night.

using System.Globalization;

const int UserBudget = 200;             // EUR a night
const int HotelAgentWindow = 128_000;   // tokens the Hotel agent's model can handle in one call

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;   // 125,000 on every machine
Console.WriteLine("Fallacy 3: Bandwidth is infinite");
Console.WriteLine();
Naive();
Console.WriteLine();
Fixed();

// Naive: hand over everything, and let the runtime cut it down to fit.
void Naive()
{
    Console.WriteLine("Naive: forward every review and the whole run history");
    var researchAgent = new ResearchAgent();

    var reviews = researchAgent.ReturnEveryReview();                                      // (1)
    Console.WriteLine($"  Research agent returns every review: {reviews.Tokens:N0} tokens");

    var hotelAgent = new HotelAgent(HotelAgentWindow, onOverflow: Overflow.DropOldest);   // (2)
    List<Message> history =
    [
        new("user: trip to London, March 1-3; a hotel near the office, under 200 EUR a night",
            Tokens: 40, Budget: UserBudget),
        new("flight search history", Tokens: 6_000),
        reviews,
        new("orchestrator: book the best-reviewed hotel", Tokens: 20),
    ];
    var call = hotelAgent.Decide(history);                                                // (3)

    PrintResult(call);
}

// Fixed: references instead of payloads, constraints inside the task, and overflow as an error.
void Fixed()
{
    Console.WriteLine("Fixed: a summary and a reference, with the budget in the task");
    var researchAgent = new ResearchAgent();
    var store = new Dictionary<string, Message>();   // the Claim Check: the full reviews stay here

    var summary = researchAgent.StoreReviewsAndSummarize(store, maxTokens: 300);
    Console.WriteLine($"  Research agent stores the reviews as {summary.Reference}"
        + $" and returns a summary: {summary.Tokens:N0} tokens");

    var hotelAgent = new HotelAgent(HotelAgentWindow, onOverflow: Overflow.Fail);
    List<Message> task =
    [
        new("orchestrator: book a hotel near the office for March 1-3, under 200 EUR a night",
            Tokens: 30, Budget: UserBudget),
        summary,
    ];

    try
    {
        PrintResult(hotelAgent.Decide(task));
    }
    catch (ContextTooLargeException)
    {
        Console.WriteLine("  Result: no booking yet; ask for a shorter summary or split the task");
    }
}

void PrintResult(BookHotelCall call)
{
    var verdict = call.PricePerNight <= UserBudget
        ? $"within the {UserBudget} EUR budget"
        : $"over the {UserBudget} EUR budget the user set at the start";
    Console.WriteLine($"  Result: {call.PricePerNight} EUR a night, {verdict}");
}

// ----- The simulated world ------------------------------------------------------------------

// A message as the model sees it: the budget and the hotels its text carries, if any.
record Message(string Text, int Tokens, int? Budget = null, Hotel[]? Hotels = null, string? Reference = null);

// A tool call as data: what the model wants done.
record BookHotelCall(string Hotel, int PricePerNight);

record Hotel(string Name, int PricePerNight, double Rating);

// Stands in for the Research agent: it has read 125,000 tokens of reviews.
sealed class ResearchAgent
{
    private static readonly Hotel[] TopThree =
    [
        new("Regent", 340, 4.8),
        new("Garden Inn", 180, 4.5),
        new("Station Hotel", 150, 3.9),
    ];

    public Message ReturnEveryReview() =>
        new("Research agent: every review of 40 hotels", Tokens: 125_000, Hotels: TopThree);

    public Message StoreReviewsAndSummarize(Dictionary<string, Message> store, int maxTokens)
    {
        var reference = "reviews/trip-42";
        store[reference] = ReturnEveryReview();
        var summary = new Message($"Research agent: top 3 near the office, with prices and ratings. Full reviews: {reference}",
            Tokens: 120, Hotels: TopThree, Reference: reference);
        return summary.Tokens <= maxTokens ? summary : throw new InvalidOperationException("summary over its size limit");
    }
}

enum Overflow { DropOldest, Fail }

sealed class ContextTooLargeException(string message) : Exception(message);

// Stands in for the Hotel agent: a runtime with a fixed context window, and a model that books the
// best-rated hotel it can see, within the budget if the budget is still in its window.
sealed class HotelAgent(int window, Overflow onOverflow)
{
    public BookHotelCall Decide(List<Message> messages)
    {
        var total = messages.Sum(m => m.Tokens);
        Console.WriteLine($"  Hotel agent receives {messages.Count} messages: {total:N0} tokens for a {window:N0}-token window");

        if (total > window && onOverflow == Overflow.Fail)
        {
            Console.WriteLine("    runtime: refused the task with an error, nothing dropped");
            throw new ContextTooLargeException($"the task is {total:N0} tokens for a {window:N0}-token window");
        }

        var visible = new List<Message>(messages);
        while (visible.Sum(m => m.Tokens) > window)
        {
            Console.WriteLine($"    runtime: dropped the oldest message, no error: \"{visible[0].Text}\"");
            visible.RemoveAt(0);
        }

        var budget = visible.Min(m => m.Budget);
        var hotel = visible.SelectMany(m => m.Hotels ?? [])
            .Where(h => budget is null || h.PricePerNight <= budget)
            .MaxBy(h => h.Rating) ?? throw new InvalidOperationException("no hotel left in the window");
        Console.WriteLine($"  Hotel agent asks for BookHotel({hotel.Name}, {hotel.PricePerNight} EUR a night)");
        return new BookHotelCall(hotel.Name, hotel.PricePerNight);
    }
}
