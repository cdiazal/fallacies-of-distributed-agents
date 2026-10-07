// Fallacy 8: The network is homogeneous.
// Run from the entry folder: dotnet run dotnet/NetworkIsHomogeneous.cs   (.NET 10, no dependencies)
//
// The example system with one change: two providers serve the Flight agent's model, and the runtime sends
// its calls to the cheaper one. Both accept its reply schema; only provider A enforces "pattern".

using System.Globalization;
using System.Text.RegularExpressions;

const string BookTheFlight = "step-1: book FL101 for trip-42";
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;   // "March", "01/03/2027", and Gregorian years everywhere

Console.WriteLine("Fallacy 8: The network is homogeneous");
Console.WriteLine();
Naive();
Console.WriteLine();
Fixed();

// Naive: the runtime passes on the Flight agent's reply as the provider returns it.
void Naive()
{
    Console.WriteLine("Naive: route the Flight agent's model to the cheaper provider, and pass on what it replies");
    var (airline, hotels) = (new Airline(), new Hotels());
    var runtime = new AgentRuntime(airline, hotels, contract: null);           // (1)
    Provider[] byPrice = [new("provider B", Enforces: ["type", "required"]),   // (2)
                          new("provider A", Enforces: ["type", "required", "pattern"])];
    var hotelAgent = new HotelAgent();                                         // (3)

    if (runtime.RunFlightStep(new FlightAgent(), byPrice, BookTheFlight) is { } arrival)
        runtime.RunHotelStep(hotelAgent, arrival);
    PrintResult(airline, hotels);
}

// Fixed: the runtime parses every reply itself, and asks the next provider when one fails.
void Fixed()
{
    Console.WriteLine("Fixed: parse every reply on our side, and ask the next provider when one fails");
    var (airline, hotels) = (new Airline(), new Hotels());
    var runtime = new AgentRuntime(airline, hotels, contract: new ReplyContract());   // on our side
    Provider[] byPrice = [new("provider B", Enforces: ["type", "required"]),
                          new("provider A", Enforces: ["type", "required", "pattern"])];
    var hotelAgent = new HotelAgent();

    if (runtime.RunFlightStep(new FlightAgent(), byPrice, BookTheFlight) is not { } arrival)
    {
        Console.WriteLine("  Result: FL101 booked, but no hotel for trip-42;"
            + " no provider's reply met the Flight agent's contract");
        return;
    }

    runtime.RunHotelStep(hotelAgent, arrival);
    PrintResult(airline, hotels);
}

void PrintResult(Airline airline, Hotels hotels) =>
    Console.WriteLine($"  Result: {airline.Booked!.Flight} lands on {airline.Booked.Lands:MMMM d, yyyy},"
        + $" and the Garden Inn is booked for {Hotels.Stay(hotels.CheckIn, 2)} (B-001)");

// ----- The simulated world ------------------------------------------------------------------

// A tool call as data: what the model wants done.
record ToolCall(string Tool, string Argument);

// What the airline confirms, typed; the runtime hands it to the Flight agent's model as the tool result.
record Confirmation(string Flight, string Route, DateOnly Lands);

// The Flight agent's reply, as its schema asks for it. JSON has no date type, so the arrival is text.
record Reply(string Flight, string Arrival)
{
    public const string ArrivalPattern = "^[0-9]{4}-[0-9]{2}-[0-9]{2}$";   // the schema's "pattern"
}

// The Flight agent's model: book the step's flight, then reply with its landing day, day first unless forced.
sealed class FlightAgent
{
    public ToolCall Decide(string step) => new("BookFlight", step.Split(' ')[2]);

    public Reply Answer(Confirmation booked, Func<string, bool> allowed) =>
        booked.Lands.ToString("dd/MM/yyyy") is var dayFirst && allowed(dayFirst)
            ? new(booked.Flight, dayFirst) : new(booked.Flight, booked.Lands.ToString("yyyy-MM-dd"));
}

// The Hotel agent's model: book 2 nights from the task's date, reading slashes month first when it can.
sealed class HotelAgent
{
    public ToolCall Decide(string task)
    {
        var text = task.Split(" from ")[^1].TrimEnd('.');
        if (text.Contains('/') && text.Split('/').Select(int.Parse).ToArray() is [var first, var second, var year])
            text = (first <= 12 ? new DateOnly(year, first, second) : new(year, second, first)).ToString("yyyy-MM-dd");
        return new("BookHotel", $"Garden Inn, {text}, 2 nights");
    }
}

// A provider serving the Flight agent's model: it enforces the schema keywords it supports, and ignores the rest.
sealed record Provider(string Name, string[] Enforces)
{
    public Reply Generate(Func<Func<string, bool>, Reply> model) =>
        model(text => !Enforces.Contains("pattern") || Regex.IsMatch(text, Reply.ArrivalPattern));

    public string Did => Enforces.Contains("pattern")
        ? "accepted the schema and enforced its \"pattern\""
        : "accepted the schema but ignored its \"pattern\", no error";
}

// Ours, not the provider's: the reply's arrival must be a real date, written YYYY-MM-DD.
sealed class ReplyContract
{
    public DateOnly? Parse(Reply reply) => Regex.IsMatch(reply.Arrival, Reply.ArrivalPattern)
        && DateOnly.TryParseExact(reply.Arrival, "yyyy-MM-dd", out var arrival) ? arrival : null;
}

// The agents' runtime: it routes model calls, runs tool calls, and fills step-2's date from the Flight agent's reply.
sealed class AgentRuntime(Airline airline, Hotels hotels, ReplyContract? contract)
{
    public string? RunFlightStep(FlightAgent agent, Provider[] byPrice, string step)
    {
        Console.WriteLine($"  runtime: sends the Flight agent's model calls to {byPrice[0].Name}, the cheaper of the two");
        var call = agent.Decide(step);
        Console.WriteLine($"  Flight agent asks for {call.Tool}({call.Argument})");
        var booked = airline.Book(call);
        foreach (var (i, provider) in byPrice.Index())
        {
            var reply = provider.Generate(allowed => agent.Answer(booked, allowed));
            Console.WriteLine($"  Flight agent replies through {provider.Name}: {{\"flight\": \"{reply.Flight}\", \"arrival\": \"{reply.Arrival}\"}}");
            Console.WriteLine($"    {provider.Name}: {provider.Did}");
            if (contract is null) return reply.Arrival;
            if (contract.Parse(reply) is { } arrival)
            {
                Console.WriteLine($"    runtime: parsed arrival as {arrival:MMMM d, yyyy}");
                return arrival.ToString("yyyy-MM-dd");
            }

            var then = i + 1 < byPrice.Length ? $"asks {byPrice[i + 1].Name} for the reply" : "no provider left";
            Console.WriteLine($"    runtime: refused the reply: arrival \"{reply.Arrival}\" isn't a YYYY-MM-DD date; {then}");
        }

        return null;
    }

    public void RunHotelStep(HotelAgent agent, string arrival)
    {
        var task = $"Book the Garden Inn for 2 nights from {arrival}.";
        Console.WriteLine($"  runtime: hands step-2 to the Hotel agent: \"{task}\"");
        var call = agent.Decide(task);
        Console.WriteLine($"  Hotel agent asks for {call.Tool}({call.Argument})");
        hotels.Book(call);
    }
}

// The airline behind BookFlight. It confirms each booking with a typed date.
sealed class Airline
{
    public Confirmation? Booked { get; private set; }

    public Confirmation Book(ToolCall call)
    {
        Booked = new(call.Argument, "Madrid 07:10 to London 08:35", new(2027, 3, 1));
        Console.WriteLine($"    airline: booked {Booked.Flight}, {Booked.Route}, lands {Booked.Lands:MMMM d, yyyy}");
        return Booked;
    }
}

// The hotels behind BookHotel. A booking needs a real check-in date, written YYYY-MM-DD.
sealed class Hotels
{
    public DateOnly CheckIn { get; private set; }

    public void Book(ToolCall call)
    {
        var (name, checkIn, nights) = call.Argument.Split(", ") switch { var a => (a[0], a[1], int.Parse(a[2][..1])) };
        CheckIn = DateOnly.ParseExact(checkIn, "yyyy-MM-dd");
        Console.WriteLine($"    hotels: booked B-001 ({name}, {Stay(CheckIn, nights)}, {180 * nights} EUR)");
    }

    // How the trace writes a stay: "March 1-3", or "January 31-February 2" across months.
    public static string Stay(DateOnly checkIn, int nights)
    {
        var checkOut = checkIn.AddDays(nights);
        return $"{checkIn:MMMM d}-{(checkOut.Month == checkIn.Month ? $"{checkOut.Day}" : $"{checkOut:MMMM d}")}";
    }
}
