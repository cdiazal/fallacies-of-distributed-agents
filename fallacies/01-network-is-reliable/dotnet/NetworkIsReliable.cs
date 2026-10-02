// Fallacy 1: The network is reliable.
// Run from the entry folder: dotnet run dotnet/NetworkIsReliable.cs   (.NET 10, no dependencies)
//
// The orchestrator of a travel assistant asks its Flight agent to book one flight.
// The airline books the seat, but the reply to the first call is lost on the way back.

const string Request = "Book one flight from Madrid to London on March 1, 2027";

Console.WriteLine("Fallacy 1: The network is reliable");
Console.WriteLine();
Naive();
Console.WriteLine();
Fixed();

// Naive: after a timeout, the orchestrator runs the whole agent turn again.
void Naive()
{
    Console.WriteLine("Naive: after a timeout, ask the Flight agent again");
    var airline = new Airline();
    var network = new Network(loseReplyOnCall: 1);
    var flightAgent = new FlightAgent();

    for (var attempt = 1; attempt <= 3; attempt++)
    {
        Console.WriteLine($"  attempt {attempt}");
        try
        {
            var call = flightAgent.Decide(Request);                              // (1)
            var booking = network.Call(() => airline.BookFlight(call.Flight));   // (2)
            Console.WriteLine($"    orchestrator: confirmed {booking.Flight}");
            break;
        }
        catch (TimeoutException)
        {
            Console.WriteLine("    orchestrator: no reply, will retry");         // (3)
        }
    }

    PrintResult(airline);
}

// Fixed: the agent decides once; only the tool call is retried, with an idempotency key.
void Fixed()
{
    Console.WriteLine("Fixed: decide once, retry only the tool call, with an idempotency key");
    var airline = new Airline();
    var network = new Network(loseReplyOnCall: 1);
    var flightAgent = new FlightAgent();

    var call = flightAgent.Decide(Request);   // decide once, and keep the tool call
    var idempotencyKey = "trip-42/step-1";    // run id + step id, saved with the step
    Console.WriteLine($"  Flight agent asks for BookFlight({call.Flight}); idempotency key {idempotencyKey}");

    Booking? booking = null;
    for (var attempt = 1; attempt <= 3; attempt++)
    {
        Console.WriteLine($"  attempt {attempt}");
        try
        {
            booking = network.Call(() => airline.BookFlight(call.Flight, idempotencyKey));
            Console.WriteLine($"    orchestrator: confirmed {booking.Flight}");
            break;
        }
        catch (TimeoutException)
        {
            Console.WriteLine("    Flight agent: no reply from BookFlight, will retry");
        }
    }

    if (booking is null)
    {
        Console.WriteLine($"    Flight agent: outcome unknown; keep {idempotencyKey} and check before booking again");
    }

    PrintResult(airline);
}

void PrintResult(Airline airline)
{
    var count = airline.Bookings.Count;
    var flights = string.Join(", ", airline.Bookings.Select(b => b.Flight));
    Console.WriteLine($"  Result: {count} {(count == 1 ? "booking" : "bookings")} for 1 request ({flights})");
}

// ----- The simulated world ------------------------------------------------------------------

// Stands in for the language model behind the Flight agent. Like a real model, it books nothing:
// it returns the tool call it wants made. Asked again, it doesn't have to ask for the same one.
sealed class FlightAgent
{
    private readonly Queue<string> _scriptedFlights = new(["FL101", "FL207"]);

    public BookFlightCall Decide(string request) => new(_scriptedFlights.Dequeue());
}

// A tool call as data: what the model wants done. The runtime decides how to execute it.
record BookFlightCall(string Flight);

record Booking(string Id, string Flight);

// The remote system behind the BookFlight tool.
sealed class Airline
{
    private readonly Dictionary<string, Booking> _bookingsByKey = [];

    public List<Booking> Bookings { get; } = [];

    public Booking BookFlight(string flight, string? idempotencyKey = null)
    {
        if (idempotencyKey is not null && _bookingsByKey.TryGetValue(idempotencyKey, out var existing))
        {
            if (existing.Flight != flight)
            {
                throw new InvalidOperationException($"key {idempotencyKey} was already used for {existing.Flight}");
            }

            Console.WriteLine($"    airline: key {idempotencyKey} already used, returns {existing.Id}");
            return existing;
        }

        var booking = new Booking($"B-{Bookings.Count + 1:000}", flight);
        Bookings.Add(booking);
        if (idempotencyKey is not null)
        {
            _bookingsByKey[idempotencyKey] = booking;
        }

        Console.WriteLine($"    airline: booked {flight} as {booking.Id}");
        return booking;
    }
}

// Delivers every request, but loses the reply to one call:
// the remote side has done the work, the caller only sees a timeout.
sealed class Network(int loseReplyOnCall)
{
    private int _calls;

    public T Call<T>(Func<T> remoteOperation)
    {
        _calls++;
        var reply = remoteOperation();
        if (_calls == loseReplyOnCall)
        {
            throw new TimeoutException();
        }

        return reply;
    }
}
