# 2. Latency is zero

> *"Latency is zero."* — the second of the eight fallacies of distributed computing, from L. Peter
> Deutsch and colleagues at Sun Microsystems.

## The fallacy

A call to another machine isn't a function call that happens to go further. It waits for the network in
both directions, every time. Inside one data center a round trip takes about half a millisecond; across
an ocean, about 150 milliseconds. A read from main memory takes about 100 nanoseconds. That's a gap of
three to six orders of magnitude, and more bandwidth doesn't close it: latency comes from distance and
hops, not from capacity.

Believing latency is zero means designing remote calls as if they were local: chatty interfaces that
fetch one field at a time, loops that call a service once per item, and steps that run one after another
because that's how the code was written, not because they depend on each other. Each call looks cheap on
its own. The user waits for the sum.

The classic answers start with not calling at all: a call you don't make has no latency, so keep what you
read often close, in a cache or a local copy. When you do call, make fewer, coarser calls, sending
everything a step needs in one request. Run independent calls in parallel, so that you wait for the
slowest one instead of the sum. And give the whole operation a deadline and pass it down with every call,
so that work stops when nobody is waiting for it.

Jim Waldo and his colleagues at Sun called latency the most obvious difference between a local call and a
remote one, and warned in 1994 that a design that ignores it is "virtually assured of having performance
problems."

## The picture

![Left: a client makes three round trips to a server where one would do, and waits for the network three
times; the fix is to cache what it can and make fewer, coarser calls, in parallel when independent.
Right: the orchestrator asks the Flight, Hotel, and Research agents six small questions, one turn after
another, against the user's 20-second deadline, and the last two turns end after the user has gone; the
fix is one message per task and independent tasks in parallel, which gets the plan ready at 10 seconds,
and the deadline passed down, so that no agent works past it.](diagram.excalidraw.svg)

## With many agents

In a multi-agent system, every call to an agent starts an agent turn, and a turn costs seconds, not
milliseconds. The network still adds its round trip, but most of the wait is the model: reading
everything it was sent, then generating its answer token by token. Each tool call inside the turn adds
the tool's own wait plus another model call to read the result.

Agents also make the chatty, sequential design the natural one. An agent works in a loop: it decides,
acts, reads the result, and decides again, so every step waits for the one before. An orchestrator built
the same way delegates one small question at a time ("search flights to London," then "pick the morning
flight"), and every question costs two turns: the agent's, to answer it, and the orchestrator's own, to
read the answer and decide what to ask next.

Two things are new. First, the latency is in the turns, not in the wires: a faster network won't fix it;
fewer and shorter turns will, with more of them running at once. And when the orchestrator is a model,
which turns run together is its choice: only the agents it asks for in the same turn can work at the same
time. Second, the work nobody waits for is still billed. A chat app or an API gateway gives up when its
timeout fires (Amazon API Gateway waits
[29 seconds by default](https://docs.aws.amazon.com/apigateway/latest/api/API_Integration.html)), but the
deadline doesn't travel with the request, so the agents keep working, and spending tokens, on an answer
nobody will read.

## In code

Both programs simulate the [example system](../../docs/example-system.md): to plan a trip, the
orchestrator needs a flight, a hotel, the hotel's reviews, and the visa rules; the user's app gives up
after 20 seconds. Each agent turn takes four to six seconds, and the orchestrator's own turns are left
out. Time is simulated, so the programs run instantly and print the same every time. Each has no
dependencies and runs a naive version, then a fixed one. From this folder:

```text
dotnet run dotnet/LatencyIsZero.cs      # .NET 10
python python/latency_is_zero.py        # Python 3.12+
```

The naive orchestrator asks one agent at a time, in small steps, and never tells the agents about the
deadline ([full .NET file](dotnet/LatencyIsZero.cs), [full Python file](python/latency_is_zero.py)):

```csharp
var now = 0;
now = timeline.Ask(flightAgent, "search flights to London", startAt: now);      // (1)
now = timeline.Ask(flightAgent, "pick the morning flight", startAt: now);       // (2)
now = timeline.Ask(hotelAgent, "search hotels near the office", startAt: now);
now = timeline.Ask(hotelAgent, "pick the one with breakfast", startAt: now);
now = timeline.Ask(researchAgent, "check the hotel's reviews", startAt: now);   // (3)
now = timeline.Ask(researchAgent, "check the visa rules", startAt: now);
```

```python
now = 0
now = timeline.ask(flight_agent, "search flights to London", start_at=now)      # (1)
now = timeline.ask(flight_agent, "pick the morning flight", start_at=now)       # (2)
now = timeline.ask(hotel_agent, "search hotels near the office", start_at=now)
now = timeline.ask(hotel_agent, "pick the one with breakfast", start_at=now)
now = timeline.ask(research_agent, "check the hotel's reviews", start_at=now)   # (3)
now = timeline.ask(research_agent, "check the visa rules", start_at=now)
```

Both programs print the same output. The naive half:

```text
Naive: one agent at a time, one small question at a time
   0 s ->  4 s  Flight agent: search flights to London
   4 s ->  8 s  Flight agent: pick the morning flight
   8 s -> 12 s  Hotel agent: search hotels near the office
  12 s -> 16 s  Hotel agent: pick the one with breakfast
  16 s -> 22 s  Research agent: check the hotel's reviews   <- the user has gone
  22 s -> 28 s  Research agent: check the visa rules   <- the user has gone
  Result: plan ready at 28 s, but the user gave up at 20 s; 2 agent turns ended after that
```

## Where it breaks

**(1) Each question waits for the previous answer.** The flight, the hotel, and the visa rules don't
depend on each other, yet each one starts only when the last one ends, so the seconds add up: 28 in all.

**(2) Each task is split into two small questions.** "Search flights" and then "pick the morning flight"
are two full agent turns where one would do, and the first one writes out every flight it found, token by
token, just so the second can pick one.

**(3) The deadline stays with the user.** At 20 seconds the user's app gives up, but nothing tells the
agents. The reviews and the visa rules end after the user has gone, and the plan is ready for nobody.

The fixed version still takes four to six seconds per turn; it just waits for two in a row instead of
six. Its deadline never fires here, but a turn that ran long would stop at 20 seconds instead of working
for nobody.

### How to avoid it

Wait for the longest chain, not the sum. Send each agent its whole task in one message ("find the
morning flight to London"), the way a [*Remote Facade*](https://martinfowler.com/eaaCatalog/remoteFacade.html)
(Martin Fowler) turns a chatty interface into one coarse call. Start independent tasks at the same time,
and chain only the ones that really depend on another: here, the reviews need the chosen hotel, so the
plan waits for that chain, four seconds for the hotel and then six for the reviews. And pass the deadline
down with every call, as a point in time rather than a timeout, so that every hop knows how much is left.
Keep it in the runtime, not in the prompt: a model told "answer within 20 seconds" can't see the clock,
but a cancellation token can stop its call.

```csharp
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
```

```python
# The flight, the hotel, and the visa rules don't depend on each other: ask for them together.
flight = timeline.ask(flight_agent, "find the morning flight to London",
                      start_at=0, deadline=USER_DEADLINE)
hotel = timeline.ask(hotel_agent, "find a hotel near the office, with breakfast",
                     start_at=0, deadline=USER_DEADLINE)
visa = timeline.ask(research_agent, "check the visa rules",
                    start_at=0, deadline=USER_DEADLINE)
# The reviews need the hotel, so they start when it's chosen.
reviews = timeline.ask(research_agent, "check the hotel's reviews",
                       start_at=hotel, deadline=USER_DEADLINE)
```

And the fixed half of the output:

```text
Fixed: one message per task, in parallel when independent, deadline passed down
   0 s ->  4 s  Flight agent: find the morning flight to London
   0 s ->  4 s  Hotel agent: find a hotel near the office, with breakfast
   0 s ->  6 s  Research agent: check the visa rules
   4 s -> 10 s  Research agent: check the hotel's reviews
  Result: plan ready at 10 s, within the 20 s deadline
```

When the deadline can't be met, return what you have and say what's missing, instead of nothing; the
fixed code does that if a turn is canceled. For that to work, give the agents a deadline a little earlier
than the user's, so that there's time left to answer. Better still, stream each part as it's ready: the
user sees the flight at 4 seconds instead of a spinner until 10.

In real code, the same shape in .NET is `Task.WhenAll` with a `CancellationToken` that carries the
deadline (`CancellationTokenSource.CancelAfter`); in Python, it's an `asyncio.TaskGroup` inside
`asyncio.timeout()`. Neither crosses the network, though: an agent in another process needs the time left
sent with each request, the way [gRPC propagates deadlines](https://grpc.io/docs/guides/deadlines/).
Microsoft Agent Framework builds the shape from
[fan-out and fan-in edges](https://learn.microsoft.com/en-us/agent-framework/concepts/workflows/edges) in
a workflow; its [concurrent orchestration](https://learn.microsoft.com/en-us/agent-framework/workflows/orchestrations/concurrent)
is the special case that sends the same input to every agent. And check what runs in series without
asking: when a model asks for several tools in one turn, Microsoft.Extensions.AI's
`FunctionInvokingChatClient` runs them one after another unless you set
[`AllowConcurrentInvocation`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.functioninvokingchatclient.allowconcurrentinvocation)
to `true`.

The example leaves out three things to stay short. Its turns always take the same time, and real ones
don't: the more agents run at once, the more likely one has a slow turn, and the fan-out waits for it.
Parallel turns spend the same tokens as sequential ones, but they reach the model provider's rate limits
sooner, which brings back the `429` responses of [fallacy 1](../01-network-is-reliable/). And a turn
takes longer the more the agent reads and writes, so sending less also makes it faster; that's
[fallacy 3](../03-bandwidth-is-infinite/).

---

**Read more**

- [The Eight Fallacies of Distributed Computing](https://nighthacks.com/jag/res/Fallacies.html) (L. Peter
  Deutsch, on James Gosling's site)
- [Fallacy #2: Latency is zero](https://particular.net/blog/latency-is-zero) (David Boike, Particular
  Software)
- [A Note on Distributed Computing](https://doc.akka.io/libraries/misc/smli_tr-94-29.pdf) (Jim Waldo,
  Geoff Wyant, Ann Wollrath, and Sam Kendall, Sun Microsystems Laboratories, 1994)
- [Designs, Lessons and Advice from Building Large Distributed Systems](https://www.cs.cornell.edu/projects/ladis2009/talks/dean-keynote-ladis2009.pdf)
  (Jeff Dean, Google, LADIS 2009), whose "Numbers Everyone Should Know" slide is the source of the
  figures above
- [The Tail at Scale](https://research.google/pubs/the-tail-at-scale/) (Jeffrey Dean and Luiz André
  Barroso, *Communications of the ACM*, 2013), on why a fan-out waits for its slowest call

[← All ten fallacies](../../README.md)
