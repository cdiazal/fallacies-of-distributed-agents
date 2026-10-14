# 3. Bandwidth is infinite

> *"Bandwidth is infinite."* — the third of the eight fallacies of distributed computing, from L. Peter
> Deutsch and colleagues at Sun Microsystems.

## The fallacy

Every link has a capacity, and every message spends some of it. A large message takes longer to send,
takes longer still when the link is shared, and crowds out everything else on the way. Past a certain
size it isn't accepted at all: an HTTP server answers `413 Content Too Large`, and a message broker
rejects anything over its limit.

Believing bandwidth is infinite means sending whatever is at hand: whole objects when a field would do,
full result sets instead of a page, the attachment instead of a link to it. Sending less, though, pulls
against the fix for [fallacy 2](../02-latency-is-zero/): to save round trips, you send more in each one;
to save bandwidth, you send less. The right size is neither the smallest nor the biggest; it's what the
receiver needs.

The classic answers are to size messages on purpose and to keep large data out of them. The *Claim
Check* pattern stores the payload and sends a reference to it, so that only the receiver that needs the
data fetches it. Paging, compression, and filtering at the source do the same job from other angles.

A congested network doesn't announce it: routers drop packets without a word, and TCP resends them and
slows down. But a message still arrives whole, arrives late, or fails with an error. It never arrives cut
short.

## The picture

![Left: a client sends a 50 MB report through a narrow link and gets 413 Content Too Large back; slow or
rejected, a message is never cut short, and the fix is a reference (Claim Check) and messages sized on
purpose. Right: the Research agent returns every review, and the orchestrator forwards the whole run
history to the Hotel agent; its 128,000-token window drops the user's 200 EUR budget and the flight
history with no error, and the Hotel agent sends BookHotel(Regent, 340 EUR a night) to the hotels; the
fix is a summary with a reference (reviews/trip-42), the budget inside the task, and an error if the task
still doesn't fit.](diagram.excalidraw.svg)

## With many agents

In a multi-agent system, the bandwidth that runs out first isn't the network's: it's the context window.
A model handles only a fixed number of tokens in one call, and everything competes for that space: the
instructions, the tool definitions, the conversation so far, every tool result and agent answer passed to
it, and the answer the model writes back. Agents fill it fast, because the easy way to hand over work is
to hand over everything: the whole run history, the full search results, every review the Research agent
read. Some frameworks make that the default: in the
[OpenAI Agents SDK](https://openai.github.io/openai-agents-python/handoffs/), an agent that receives a
handoff sees the entire previous conversation unless you pass an `input_filter`.

Two things are new. First, the overflow can be silent. A model API can reject a request that doesn't fit
([Claude's](https://platform.claude.com/docs/en/build-with-claude/context-windows) returns a 400 error),
but the layer that keeps a conversation going can trim the conversation instead, dropping or summarizing
the oldest messages, and the model carries on with whatever is left. The user's budget, stated once at the
start, sits in the oldest message of the run, so it's the first to go. Second, a long context hurts before
it overflows. Models often use what sits at the start and the end of a long context better than what sits
in the middle, and they do worse as the context grows, so a constraint the user adds halfway through a
long run can be missed even when everything fits.

## In code

Both programs simulate the [example system](../../docs/example-system.md): the Research agent has read
every review of the hotels near the office, the orchestrator asks the Hotel agent to book one, and at the
start of the run the user set a budget of 200 EUR a night. The Hotel agent's window is 128,000 tokens,
and its simulated model books the best-rated hotel it can see, within the budget if the budget is still in
its window. Each program has no dependencies and runs a naive version, then a fixed one. From this folder:

```text
dotnet run dotnet/BandwidthIsInfinite.cs      # .NET 10
python python/bandwidth_is_infinite.py        # Python 3.12+
```

The naive orchestrator hands over everything and lets the runtime cut it down to fit
([full .NET file](dotnet/BandwidthIsInfinite.cs), [full Python file](python/bandwidth_is_infinite.py)):

```csharp
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
```

```python
reviews = research_agent.return_every_review()                                   # (1)
print(f"  Research agent returns every review: {reviews.tokens:,} tokens")

hotel_agent = HotelAgent(HOTEL_AGENT_WINDOW, on_overflow=Overflow.DROP_OLDEST)   # (2)
history = [
    Message("user: trip to London, March 1-3; a hotel near the office, under 200 EUR a night",
            tokens=40, budget=USER_BUDGET),
    Message("flight search history", tokens=6_000),
    reviews,
    Message("orchestrator: book the best-reviewed hotel", tokens=20),
]
call = hotel_agent.decide(history)                                               # (3)
```

Both programs print the same output. The naive half:

```text
Naive: forward every review and the whole run history
  Research agent returns every review: 125,000 tokens
  Hotel agent receives 4 messages: 131,060 tokens for a 128,000-token window
    runtime: dropped the oldest message, no error: "user: trip to London, March 1-3; a hotel near the office, under 200 EUR a night"
    runtime: dropped the oldest message, no error: "flight search history"
  Hotel agent asks for BookHotel(Regent, 340 EUR a night)
  Result: 340 EUR a night, over the 200 EUR budget the user set at the start
```

## Where it breaks

**(1) The Research agent returns everything it read.** That's 125,000 tokens of reviews, when the Hotel
agent needs only three hotels, their prices, and their ratings.

**(2) The runtime drops the oldest messages to make room, with no error.**
[Ollama](https://github.com/ollama/ollama/blob/main/server/prompt.go) does this by default: it keeps the
system messages and the newest ones, and logs the cut only at debug level. Here the runtime keeps 125,000
tokens of reviews and drops the 40 that carried the budget.

**(3) The orchestrator forwards the whole run history.** The budget travels as an old chat message, not
inside the task, so the Hotel agent books the best-rated hotel it can see: the Regent, at 340 EUR a night.

The fixed version still reads the same 125,000 tokens of reviews; what changes is that they stay in a
store, and that only a summary, a reference, and the budget reach the Hotel agent.

### How to avoid it

Send references, not payloads. The Research agent keeps the full reviews in a store and returns a short
summary with a reference to them (`reviews/trip-42`): that's the
[*Claim Check*](https://www.enterpriseintegrationpatterns.com/patterns/messaging/StoreInLibrary.html) of
*Enterprise Integration Patterns* (Gregor Hohpe and Bobby Woolf). An agent that needs the details asks
for them by reference, with a tool that returns only the part it asks for (the Garden Inn's reviews, not
all 40 hotels), because fetching the whole claim check would bring the overflow back. Anthropic's
[multi-agent research system](https://www.anthropic.com/engineering/multi-agent-research-system) works
this way: its subagents store their work and "pass lightweight references back to the coordinator."

Give every agent's output a size limit in its contract (here, 300 tokens). A `max_tokens` cap alone
doesn't enforce it: an answer that hits the cap
[comes back cut short](https://platform.claude.com/docs/en/build-with-claude/handling-stop-reasons), and
only its stop reason says so. Send each agent its own task, with the constraints inside it, not the whole
run history: the orchestrator writes the budget into the booking task it sends, rather than counting on a
sentence the user typed an hour ago. And make overflow loud. The runtime owns the window, and the model
never learns what was cut from it, so a task that doesn't fit should fail with an error, not lose its
oldest messages.

```csharp
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
```

```python
store: dict[str, Message] = {}   # the Claim Check: the full reviews stay here

summary = research_agent.store_reviews_and_summarize(store, max_tokens=300)
print(f"  Research agent stores the reviews as {summary.reference}"
      f" and returns a summary: {summary.tokens:,} tokens")

hotel_agent = HotelAgent(HOTEL_AGENT_WINDOW, on_overflow=Overflow.FAIL)
task = [
    Message("orchestrator: book a hotel near the office for March 1-3, under 200 EUR a night",
            tokens=30, budget=USER_BUDGET),
    summary,
]

try:
    print_result(hotel_agent.decide(task))
except ContextTooLargeError:
    print("  Result: no booking yet; ask for a shorter summary or split the task")
```

And the fixed half of the output:

```text
Fixed: a summary and a reference, with the budget in the task
  Research agent stores the reviews as reviews/trip-42 and returns a summary: 120 tokens
  Hotel agent receives 2 messages: 150 tokens for a 128,000-token window
  Hotel agent asks for BookHotel(Garden Inn, 180 EUR a night)
  Result: 180 EUR a night, within the 200 EUR budget
```

If the task still doesn't fit, the runtime refuses it with an error and drops nothing, and the
orchestrator asks for a shorter summary or splits the task. Don't let the window decide what the agent
forgets.

In real code, count tokens before you send, using your model's own tokenizer, and decide what must
survive a trim. Then check what your library cuts by default. In .NET, Microsoft.Extensions.AI's
[`MessageCountingChatReducer`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.messagecountingchatreducer),
still experimental, keeps the first system message and the most recent messages, counted by number, not
by tokens, and leaves out every tool call and tool result. In Python, LangChain's
[`trim_messages`](https://reference.langchain.com/python/langchain-core/messages/utils/trim_messages)
keeps the most recent messages under a token limit, and drops the system message too unless you pass
`include_system=True`. Keep your constraints out of the part they cut.

The example leaves out three things to stay short. Every fetch through a reference is one more call,
with its own latency ([fallacy 2](../02-latency-is-zero/)). A summary loses detail, so the agent that
needs the detail must be the one that fetches it. And every token sent is paid for, on every hop and
every retry: that's [fallacy 7](../07-transport-cost-is-zero/).

---

**Read more**

- [The Eight Fallacies of Distributed Computing](https://nighthacks.com/jag/res/Fallacies.html) (L. Peter
  Deutsch, on James Gosling's site)
- [Fallacy #3: Bandwidth is infinite](https://particular.net/blog/bandwidth-is-infinite) (David Boike,
  Particular Software)
- [Lost in the Middle: How Language Models Use Long Contexts](https://aclanthology.org/2024.tacl-1.9/)
  (Nelson F. Liu et al., *Transactions of the ACL*, 2024)
- [Context Rot: How Increasing Input Tokens Impacts LLM Performance](https://www.trychroma.com/research/context-rot)
  (Kelly Hong, Anton Troynikov, and Jeff Huber, Chroma, 2025)
- Related: [The Fallacies of AI-Assisted Programming](https://writing.alteredcraft.com/p/the-fallacies-of-ai-assisted-programming)
  (Sam Keen, 2026), whose fifth fallacy is *"The context window is endless."*
- Related: [AI Agents and fallacies of Distributed Computing](https://blog.gopenai.com/ai-agents-and-fallacies-of-distributed-computing-e45fadd1c765)
  (Krishna Avva, 2026), whose section on this fallacy is titled *"context is just another payload, and a degrading one."*

[← All ten fallacies](../../README.md)
