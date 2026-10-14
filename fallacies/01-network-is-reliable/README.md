# 1. The network is reliable

> *"The network is reliable."* — the first of the eight fallacies of distributed computing, from L. Peter
> Deutsch and colleagues at Sun Microsystems.

## The fallacy

When one program calls another over a network, three things can go wrong. The request never arrives.
It arrives and the other side fails. Or it arrives, the work gets done, and the reply is lost on the
way back. From the caller's side, all three look the same: nothing comes back, and eventually a timeout
fires. A reply that is only late looks the same too, except that the work may still be running when you
retry.

Believing the network is reliable means writing a remote call as if it were a local one: no timeout, no
retry, and no plan for the moment when you don't know whether it worked. That moment is the whole
problem. A timeout tells you that you didn't hear back, not that nothing happened.

The classic answer has three parts. Put a timeout on every remote call, retry when it fires, and make
the receiver idempotent, so that handling the same request twice has the same effect as handling it
once. You need all three: without a timeout, the retry never fires; without the retry, a lost request
stays lost; and without idempotency, a lost reply turns into a duplicate.

Jim Waldo and his colleagues at Sun made the deeper point in 1994: a remote call can fail in part, with
one side done and the other unaware, and no interface that makes it look like a local call can hide that.

## The picture

![Left: a client sends a request to a server, the reply is lost, the client times out and retries, and
the server does the work twice; the fix is to keep the timeout and the retry and make the receiver
idempotent. Right: the orchestrator asks the Flight agent to book, the airline books FL101 but the reply
is lost, the orchestrator asks again, the model picks FL207, and the airline ends up with two bookings for
one request; the fix is to decide once and retry only the tool call, with an idempotency
key.](diagram.excalidraw.svg)

## With many agents

In a multi-agent system, the network is every hop: the orchestrator calling an agent, the agent calling
its model, a tool call reaching a booking API or another agent. Frameworks make this easy to forget,
because a tool or another agent is declared as a function and called like one. Model calls are slow and
rate-limited, so timeouts and `429 Too Many Requests` are routine, not exceptional.

And every layer retries on its own. The model SDK retries the model call, which costs tokens but books
nothing. The dangerous layers are the ones around the tool call: the HTTP client inside the tool, the
framework's tool loop, and the orchestrator. Three layers with three attempts each can turn one failing
call into 27 calls to `BookFlight`.

Two things are new. First, to the framework every tool looks the same: `SearchFlights` and `BookFlight`
are both a function with a JSON schema, so a retry that is harmless for the search is applied to the
booking too. Second, the easiest retry is to ask the agent again, and that runs the model again.
Frameworks such as Microsoft.Extensions.AI and the OpenAI Agents SDK hand a failed tool call back to the
model and let it decide what to do next, and an orchestrator that times out asks again from outside.
Either way, it isn't even the same question: the agent searches again, the fares have moved, and its
history now holds a timeout. The first attempt books FL101, the second books FL207, and nothing on the
airline's side can tell that they belong to the same step.

## In code

Both programs simulate the [example system](../../docs/example-system.md): the orchestrator asks the
Flight agent to book one flight; the airline books it, but the reply to the first call is lost. Each has
no dependencies and runs a naive version, then a fixed one. From this folder:

```text
dotnet run dotnet/NetworkIsReliable.cs      # .NET 10
python python/network_is_reliable.py        # Python 3.12+
```

The naive orchestrator retries the whole agent turn after a timeout
([full .NET file](dotnet/NetworkIsReliable.cs), [full Python file](python/network_is_reliable.py)):

```csharp
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
```

```python
for attempt in range(1, 4):
    print(f"  attempt {attempt}")
    try:
        call = flight_agent.decide(REQUEST)                                # (1)
        booking = network.call(lambda: airline.book_flight(call.flight))   # (2)
        print(f"    orchestrator: confirmed {booking.flight}")
        break
    except TimeoutError:
        print("    orchestrator: no reply, will retry")                    # (3)
```

Both programs print the same output. The naive half:

```text
Naive: after a timeout, ask the Flight agent again
  attempt 1
    airline: booked FL101 as B-001
    orchestrator: no reply, will retry
  attempt 2
    airline: booked FL207 as B-002
    orchestrator: confirmed FL207
  Result: 2 bookings for 1 request (FL101, FL207)
```

## Where it breaks

**(1) The retry runs the model again.** Asking the agent a second time isn't resending the request: the
model makes a new choice, and this time it picks FL207. A model that always gave the same answer
wouldn't help, because of (2): the airline would book FL101 twice.

**(2) The tool call carries no idempotency key.** The airline can't tell a retry from a new booking, so
it books again.

**(3) A timeout doesn't mean "not done."** The airline booked FL101 before the reply was lost, but the
orchestrator treats "I don't know" as "it failed" and redoes the whole step.

The fixed version still retries after a timeout; what changes is that the thing it retries is safe to
repeat.

### How to avoid it

Retry the effect, not the reasoning. Let the agent decide once, keep the tool call it produced, and
retry only that call. Give it an idempotency key built from the run and the step (`trip-42/step-1`), not
from its arguments, so that every retry of this step reuses it and no other step ever does. The
orchestrator creates the key when it plans the step and saves it with the step; the model never does. A
key in the tool's parameters is one more argument the model can change, and the tool call id changes
every time the model calls again.

The airline stores the booking under that key, in the same transaction as the booking itself, and a
retry with a key it has already seen gets the same booking back. A key that comes back with different
arguments isn't a retry, so it's rejected. This is the
[*Idempotent Receiver*](https://www.enterpriseintegrationpatterns.com/patterns/messaging/IdempotentReceiver.html)
from *Enterprise Integration Patterns* (Gregor Hohpe and Bobby Woolf), and it's what payment APIs such as
Stripe and Adyen expose as an `Idempotency-Key` header. If the API behind a tool takes no key, the tool
has to keep it: record the key before the call, and after a timeout look the booking up by your own
reference before booking again.

```csharp
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
```

```python
call = flight_agent.decide(REQUEST)   # decide once, and keep the tool call
idempotency_key = "trip-42/step-1"    # run id + step id, saved with the step
print(f"  Flight agent asks for BookFlight({call.flight}); idempotency key {idempotency_key}")

booking = None
for attempt in range(1, 4):
    print(f"  attempt {attempt}")
    try:
        booking = network.call(lambda: airline.book_flight(call.flight, idempotency_key))
        print(f"    orchestrator: confirmed {booking.flight}")
        break
    except TimeoutError:
        print("    Flight agent: no reply from BookFlight, will retry")

if booking is None:
    print(f"    Flight agent: outcome unknown; keep {idempotency_key} and check before booking again")
```

And the fixed half of the output:

```text
Fixed: decide once, retry only the tool call, with an idempotency key
  Flight agent asks for BookFlight(FL101); idempotency key trip-42/step-1
  attempt 1
    airline: booked FL101 as B-001
    Flight agent: no reply from BookFlight, will retry
  attempt 2
    airline: key trip-42/step-1 already used, returns B-001
    orchestrator: confirmed FL101
  Result: 1 booking for 1 request (FL101)
```

If the retries run out, the step hasn't failed; its outcome is unknown. Keep the key and check before you
let the agent plan a new step, and tell the model "outcome unknown," not "failed," or it will book again.

The example leaves out two things to stay short: a backoff with jitter between attempts, and a circuit
breaker that stops calling a tool that keeps failing (Michael Nygard,
[*Release It!*](https://pragprog.com/titles/mnee2/release-it-second-edition/)). You don't write these by
hand: [Polly](https://www.pollydocs.org/) gives .NET retries, timeouts, and circuit breakers, and
[tenacity](https://tenacity.readthedocs.io/) gives Python retries with backoff. None of them gives you
the key, though, and some retry without being asked: .NET's standard HTTP resilience handler retries POST
requests unless you call `DisableForUnsafeHttpMethods()`. Let one layer own the retry of each call: the
model SDK for model calls, tool-calling middleware for tool calls, and nothing around the whole agent
turn.

---

**Read more**

- [The Eight Fallacies of Distributed Computing](https://nighthacks.com/jag/res/Fallacies.html) (L. Peter
  Deutsch, on James Gosling's site)
- [Fallacy #1: The network is reliable](https://particular.net/blog/the-network-is-reliable) (David
  Boike, Particular Software)
- [A Note on Distributed Computing](https://doc.akka.io/libraries/misc/smli_tr-94-29.pdf) (Jim Waldo,
  Geoff Wyant, Ann Wollrath, and Sam Kendall, Sun Microsystems Laboratories, 1994)
- [Making retries safe with idempotent APIs](https://aws.amazon.com/builders-library/making-retries-safe-with-idempotent-APIs/)
  (Malcolm Featonby, Amazon Builders' Library)
- Related: [The Fallacies of AI-Assisted Programming](https://writing.alteredcraft.com/p/the-fallacies-of-ai-assisted-programming)
  (Sam Keen, 2026), whose first fallacy is *"The model output can be made deterministic."*
- Related: [AI Agents and fallacies of Distributed Computing](https://blog.gopenai.com/ai-agents-and-fallacies-of-distributed-computing-e45fadd1c765)
  (Krishna Avva, 2026), whose section on this fallacy is titled *"failure stops being binary."*

[← All ten fallacies](../../README.md)
