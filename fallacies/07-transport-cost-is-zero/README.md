# 7. Transport cost is zero

> *"Transport cost is zero."* — the seventh of the eight fallacies of distributed computing, from L.
> Peter Deutsch and colleagues at Sun Microsystems.

## The fallacy

Moving data between machines costs something, and someone pays for it. Arnon Rotem-Gal-Oz read the
fallacy two ways. Getting data onto the wire takes marshaling, which uses computer resources and adds to
the latency of [fallacy 2](../02-latency-is-zero/). And the network itself costs cash, in routers,
bandwidth, and the people who run it: "Someone, somewhere will have to pick the tab and pay these costs."

Believing transport cost is zero means chatty calls in bulky formats, and a cost that shows up late.
Serialization, David Boike notes, usually happens outside the code we profile; in the cloud it's paid in
CPU hours, and "all these pennies tend to add up." The bill then brings "monthly feedback into how stupid
we were when we designed our software."

The classic answers make calls cheaper and fewer: payloads "as small as possible" (in Boike's words),
compact formats, larger calls instead of chit-chat, caches for what repeats, and a quota for each client.
Roy Fielding accepted a known cost when he made REST stateless: each request carries "repetitive data
(per-interaction overhead)" that "cannot be left on the server in a shared context," and caches eliminate
some requests altogether ([Fielding, 2000](https://ics.uci.edu/~fielding/pubs/dissertation/rest_arch_style.htm)).

A request costs about the same the thousandth time as the first, so the bill grows in a straight line
with the traffic.

## The picture

![Left: a client sends the same small request again and again; each one costs the same, and the monthly
bill grows in a straight line; the fix is small payloads, compact formats, fewer calls, caching, and a
quota per client. Right: in a group chat limited only to 40 agent turns, the orchestrator keeps telling
the Hotel agent to find something cheaper than the Garden Inn, and every call re-sends the whole
conversation, so round 1 bills 10,100 tokens and round 20 bills 79,250; B-001 is booked after 900,250
tokens, 2.81 USD; the fix is a token budget per run, checked before every model call, which ends the chat
after round 3 at 42,600 tokens.](diagram.excalidraw.svg)

## With many agents

In a multi-agent system, every hop is a model call that a provider bills by the token, in and out.
Providers and frameworks already ship the classic answers. Every response reports its usage; prompt
caching bills a repeated prefix at a tenth of the input price or less
([Anthropic](https://platform.claude.com/docs/en/build-with-claude/prompt-caching)); OpenAI can enforce
a monthly spend limit per project ([OpenAI](https://developers.openai.com/api/docs/guides/spend-limits));
and Microsoft Agent Framework's .NET group chat stops after 40 agent turns by default. Agents need them:
in 2025, Anthropic found that its multi-agent systems "use about 15× more tokens than chats"
([Anthropic](https://www.anthropic.com/engineering/multi-agent-research-system)).

Two things are new. First, every call pays again for everything said so far. Anthropic's Messages API is
stateless, so "you always send the full conversational history"
([Anthropic](https://platform.claude.com/docs/en/build-with-claude/working-with-messages)), and keeping
it on the server doesn't change the bill: with OpenAI's `previous_response_id`, "all previous input
tokens for responses in the chain are billed as input tokens"
([OpenAI](https://developers.openai.com/api/docs/guides/conversation-state)). What repeats is no longer
a little overhead but the whole conversation, so each round costs more than the last. Second, a loop
between agents ends when two models agree, and nothing in the code makes them agree; in 2025, UC Berkeley
researchers listed "Unaware of termination conditions" among multi-agent failures
([Cemri et al.](https://arxiv.org/abs/2503.13657)). The usual limits count the wrong thing. A turn cap
counts round 20's turns like round 1's, though here they bill almost 8 times as much, and a spend limit
covers a project for a month, not a run: OpenAI's cookbook says such limits "cover overall usage" but
can't tell whether a run "can afford its next request"
([OpenAI](https://developers.openai.com/cookbook/articles/per_run_spending_controller_responses_api)).
Even Claude's [task budgets](https://platform.claude.com/docs/en/build-with-claude/task-budgets) only
show the model a countdown: they're "a soft hint, not a hard cap."

## In code

Both programs simulate the [example system](../../docs/example-system.md): the Hotel agent proposes a
hotel for trip-42, two nights under 200 EUR a night and rated 4 or better, from the three near the office
in [fallacy 3](../03-bandwidth-is-infinite/); the orchestrator reviews each proposal in a group chat.
Every proposal meets the traveler's limits, and "cheaper" is only a preference, so whenever the chat
ends, the runtime books the latest proposal. The Hotel agent's model searches under its
last proposal's price and proposes the best-rated hotel it finds, never one rated under 4; the
orchestrator's model asks for something cheaper while it can see a cheaper hotel, and accepts otherwise.
Token counts and prices (3 USD per million input tokens, 15 per million output) are simulated. Each
program has no dependencies and runs a naive version, then a fixed one. From this folder:

```text
dotnet run dotnet/TransportCostIsZero.cs      # .NET 10
python python/transport_cost_is_zero.py       # Python 3.12+
```

The naive group chat has no token budget, only the framework's default limit of 40 agent turns
([full .NET file](dotnet/TransportCostIsZero.cs), [full Python file](python/transport_cost_is_zero.py)):

```csharp
var hotels = new Hotels();
var chat = new GroupChat(hotels, tokenBudget: null);               // (1)
var hotelAgent = new HotelAgent();
var orchestrator = new Orchestrator(Instructions: TravelPolicy);   // (2)

chat.Run(Trip42, hotelAgent, orchestrator);                        // (3)
PrintResult(hotels, chat);
```

```python
hotels = Hotels()
chat = GroupChat(hotels, token_budget=None)               # (1)
hotel_agent = HotelAgent()
orchestrator = Orchestrator(instructions=TRAVEL_POLICY)   # (2)

chat.run(TRIP_42, hotel_agent, orchestrator)              # (3)
print_result(hotels, chat)
```

Both programs print the same output. The naive half:

```text
Naive: let the agents talk until the group chat's default limit of 40 agent turns
  round 1: Hotel agent searches under 200 EUR, then proposes Garden Inn, 180 EUR a night, rated 4.5
    orchestrator: "Station Hotel is cheaper. Find something cheaper."
    runtime: round 1 billed 10,100 tokens; 10,100 in all
  round 2: Hotel agent searches under 180 EUR, then proposes Garden Inn again: none rated 4 or better
    orchestrator: "Station Hotel is cheaper. Find something cheaper."
    runtime: round 2 billed 14,450 tokens; 24,550 in all
  rounds 3-20: the same search, proposal, and answer
    runtime: round 20 billed 79,250 tokens; 900,250 in all
    runtime: 40 agent turns, the chat's limit; ends the chat, no error
  runtime: runs the latest proposal, BookHotel(Garden Inn, 2 nights)
    hotels: booked B-001 (Garden Inn, 2 nights, 360 EUR)
  Result: Garden Inn booked for 360 EUR; choosing it billed 900,250 tokens, 2.81 USD
```

## Where it breaks

**(1) The chat has no token budget, only the framework's default of 40 agent turns.** Turns aren't
tokens: round 1's two turns bill 10,100 tokens, and round 20's bill 79,250.

**(2) The orchestrator's model asks for something cheaper while it can see a cheaper hotel.** The Hotel
agent's model won't propose one rated under 4. Both follow their instructions, and neither backs down.

**(3) Every model call carries the whole conversation.** The conversation never passes 24,850 tokens, but
the chat bills 900,250 for the room that round 1 proposed.

The fixed version still lets the two models disagree, and every call still carries the whole
conversation; what changes is that the runtime gives the run a token budget and checks it before each
model call.

### How to avoid it

Budget the run, not the turns. The runtime holds one token budget for the run, shared by every agent's
calls. Before each model call, it adds what it's about to send to the longest reply the model may
return (its `max_tokens`); if the sum doesn't fit in what's left, it sends nothing and ends the chat. No
model ever sets or raises the budget, and both models stay as stubborn as in the naive version, on
purpose. Count tokens, not turns: the Throttling pattern says to "Normalize resource costs for different
operations" ([Azure Architecture Center](https://learn.microsoft.com/en-us/azure/architecture/patterns/throttling)),
and here the run is the client that gets the quota. OpenAI's cookbook builds the same check, in dollars,
as a [per-run spending controller](https://developers.openai.com/cookbook/articles/per_run_spending_controller_responses_api).

```csharp
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
```

```python
hotels = Hotels()
chat = GroupChat(hotels, token_budget=TOKEN_BUDGET_PER_RUN)   # the run's, not a model's
hotel_agent = HotelAgent()
orchestrator = Orchestrator(instructions=TRAVEL_POLICY)

if chat.run(TRIP_42, hotel_agent, orchestrator) is None:
    print("  Result: no room booked for trip-42;"
          " the run stopped before the Hotel agent proposed one")
    return

print_result(hotels, chat)
```

And the fixed half of the output:

```text
Fixed: give the run a token budget of 45,000 and check it before every model call
  round 1: Hotel agent searches under 200 EUR, then proposes Garden Inn, 180 EUR a night, rated 4.5
    orchestrator: "Station Hotel is cheaper. Find something cheaper."
    runtime: round 1 billed 10,100 tokens; 10,100 in all
  round 2: Hotel agent searches under 180 EUR, then proposes Garden Inn again: none rated 4 or better
    orchestrator: "Station Hotel is cheaper. Find something cheaper."
    runtime: round 2 billed 14,450 tokens; 24,550 in all
  round 3: the same search, proposal, and answer
    runtime: round 3 billed 18,050 tokens; 42,600 in all
    runtime: the next call could bill up to 7,450 tokens; the run has 2,400 left, so it ends the chat
  runtime: runs the latest proposal, BookHotel(Garden Inn, 2 nights)
    hotels: booked B-001 (Garden Inn, 2 nights, 360 EUR)
  Result: Garden Inn booked for 360 EUR; choosing it billed 42,600 tokens, 0.14 USD
```

A budget bounds what one run can lose; it doesn't make the agents agree. If the run stops before any
proposal, nothing is booked and the traveler is told; nothing inside the run adds to the budget. Log why
each run stopped: the same proposal twice is a cheap sign of a stuck loop, which needs better
instructions, not a bigger budget. Above the run, keep a daily budget, and the provider's spend limit as
the last line of defense.

In real code, put the meter where every model call passes through. In .NET, that's a
[`DelegatingChatClient`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.delegatingchatclient)
under each agent's `FunctionInvokingChatClient`, so it also sees the tool loop's calls: before a call,
reserve the input plus `ChatOptions.MaxOutputTokens`, atomically if agents run in parallel; after it,
charge what the provider reports. Override the streaming method too, since a group chat that emits events
[streams every agent call](https://github.com/microsoft/agent-framework/blob/main/dotnet/src/Microsoft.Agents.AI.Workflows/Specialized/AIAgentHostExecutor.cs).
In Python, the cookbook's controller is that meter; the Claude Agent SDK's `max_budget_usd` checks only
after a call, and counts one `query()` call, not a run
([Claude Agent SDK](https://code.claude.com/docs/en/agent-sdk/cost-tracking)). Check one default too: in
Microsoft Agent Framework's .NET group chat, the 40-turn limit ends the chat the way an agreement does,
with the conversation as its output and no error
([manager](https://github.com/microsoft/agent-framework/blob/main/dotnet/src/Microsoft.Agents.AI.Workflows/GroupChatManager.cs),
[host](https://github.com/microsoft/agent-framework/blob/main/dotnet/src/Microsoft.Agents.AI.Workflows/Specialized/GroupChatHost.cs)),
and a custom manager that overrides `ShouldTerminateAsync` without calling the base method loses the
limit altogether.

The example leaves out three things. Prompt caching and a summarized history make each round cheaper,
not the loop shorter ([fallacy 3](../03-bandwidth-is-infinite/)). Each agent turn can call the model up
to 40 more times as its tools run
([`MaximumIterationsPerRequest`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.functioninvokingchatclient.maximumiterationsperrequest)).
And a retry re-sends the whole conversation too: that's [fallacy 1](../01-network-is-reliable/).

---

**Read more**

- [The Eight Fallacies of Distributed Computing](https://nighthacks.com/jag/res/Fallacies.html) (L. Peter
  Deutsch, on James Gosling's site)
- [Fallacy #7: Transport cost is zero](https://particular.net/blog/transport-cost-is-zero) (David Boike,
  Particular Software, 2021)
- [Fallacies of Distributed Computing Explained](https://web.archive.org/web/20070204070434/http://www.rgoarchitects.com:80/Files/fallacies.pdf)
  (Arnon Rotem-Gal-Oz, archived PDF)
- [Architectural Styles and the Design of Network-based Software Architectures, chapter 5](https://ics.uci.edu/~fielding/pubs/dissertation/rest_arch_style.htm)
  (Roy T. Fielding, 2000)
- Related: [The Fallacies of AI-Assisted Programming](https://writing.alteredcraft.com/p/the-fallacies-of-ai-assisted-programming)
  (Sam Keen, 2026), whose fallacy "The cost of code has gone to zero" says *"Agents re-send accumulated
  context on every step."*

[← All ten fallacies](../../README.md)
