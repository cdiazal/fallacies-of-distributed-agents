# 9. Business logic can and should be centralized

> Business logic can and should be centralized — one of Ted Neward's fallacies of enterprise computing.

## The fallacy

In May 2004, Ted Neward wrote up this fallacy, which a symposium attendee had suggested
([Neward, May 17, 2004](http://web.archive.org/web/20051107004144/http://neward.net/ted/weblog/index.jsp?date=20040517)).
His point: "business logic" is too loose a term to fence in, and even a rule as simple as a 40-character
limit on a name ends up in several tiers at once. The database column enforces it, and script in the
browser checks it too.

Believing the fallacy means putting a rule in one place and assuming every request passes through it.
The wish dates from client/server days, when changing a rule meant redeploying every client. What
people wanted was to define each rule once, not to run it in one place.

The classic answer is several checks, on purpose. In a follow-up, Neward checks the name rule three
times: in the browser, for speed; in the web tier, because anyone can write an HTTP request by hand; and
in the database. Readers wanted the checks derived from one definition; he knew of no such tool
([Neward, May 18, 2004](http://web.archive.org/web/20051107004212/http://neward.net/ted/weblog/index.jsp?date=20040518)).
Answering a reader, he argued that since no enterprise can keep every rule in one place, its systems
should be built to expect rules in several
([Neward, May 19, 2004](http://web.archive.org/web/20051107004243/http://neward.net/ted/weblog/index.jsp?date=20040519)).
By 2016 he'd centralize when it saves more than it costs, but check outside data at every entry point it
comes through ([Neward, 2016](https://blogs.newardassociates.com/blog/2016/enterprise-computing-fallacies.html)).

In a classic system, code puts the server on every request's path, so its check sees every request; the
browser's copy only saves a round trip.

## The picture

![Left: a browser checks that a name has at most 40 characters, but a hand-written HTTP request skips the
browser, the web server doesn't check, and the database's wide column stores a 57-character name with no
error; the fix is to define the rule once and check it in the browser for speed and again in the server
and the database. Right: the approval rule lives only in the orchestrator's instructions; the
orchestrator asks the Hotel agent to book 6 rooms, with no amount, and the Hotel agent books B-001 and
hands off straight to the Payment agent, past the orchestrator, so Charge(2,160 EUR, GARDEN-INN-001) goes
through as CH-001 and no one is asked to approve it; the fix has Charge run the rule for every caller and
hold the payment for the travel manager's approval (AR-001), and writes the instructions from the same
rule.](diagram.excalidraw.svg)

## With many agents

In a multi-agent system, frameworks and providers already ship the classic answer: a check on the tool,
whoever calls it. The OpenAI Agents SDK's tool guardrails "run every time that tool is invoked"
([OpenAI](https://openai.github.io/openai-agents-python/guardrails/)), Microsoft Agent Framework pauses a
handoff workflow when an agent calls a tool that needs approval
([Microsoft](https://learn.microsoft.com/en-us/agent-framework/workflows/orchestrations/handoff)), and
Stripe says its agent-tagged keys "are automatically subject to approval rules"
([Stripe](https://docs.stripe.com/keys#agent-keys)). Centralizing in the orchestrator, the obvious
center, makes things worse, because it puts the rule in the one agent a handoff skips.

Two things are new. First, the center isn't on the path. A middle tier sees every request because code
puts it there; an orchestrator that hands off is only where a run starts. In a handoff workflow, "agents
are connected directly without an orchestrator," and "Only user and agent messages are synchronized," so
the orchestrator's instructions stay behind. No attacker is needed, unlike in
[fallacy 4](../04-network-is-secure/): a model picks the next agent at run time. Second, the amount
doesn't exist yet when the center speaks. A classic payment request carries its amount; here the
orchestrator's only message says "Book 6 rooms," and 2,160 EUR first appears in BookHotel's result,
inside the Hotel agent's turn.

That's why a central check misses the payment even when it's code, not a prompt. In the OpenAI Agents
SDK, "Input guardrails run only for the first agent in the chain," so a check at the orchestrator reads a
task with no amount. For workflows with managers or handoffs, the same page tells you to "use tool
guardrails instead of relying only on agent-level input/output guardrails."

## In code

Both programs simulate the [example system](../../docs/example-system.md) with one change: the agents
run as a handoff workflow, so the Hotel agent can hand off to the Payment agent. In trip-42, the Hotel
agent books 6 rooms at the Garden Inn for 2,160 EUR, and the travel policy has one rule, defined once:
payments over 2,000 EUR need the travel manager's approval. The orchestrator's model hands the booking to
the Hotel agent and stops any payment its instructions say needs approval, unless they leave it to
Charge; the Hotel agent's model books the rooms and hands the payment to the Payment agent rather than
back to the orchestrator; the Payment agent's model pays what the handoff names. Each program has no
dependencies and runs a naive version, then a fixed one. From this folder:

```text
dotnet run dotnet/LogicCanBeCentralized.cs      # .NET 10
python python/logic_can_be_centralized.py       # Python 3.12+
```

The naive runtime writes the rule only into the orchestrator's instructions, and runs Charge for any
amount ([full .NET file](dotnet/LogicCanBeCentralized.cs),
[full Python file](python/logic_can_be_centralized.py)):

```csharp
var (hotels, gateway) = (new Hotels(), new PaymentGateway());
var rule = new ApprovalRule(OverEur: 2_000, Approver: "the travel manager");
var runtime = new AgentRuntime(hotels, gateway, rule, ruleIn: Place.Instructions);   // (1)
var hotelAgent = new HotelAgent(Handoffs: ["orchestrator", "Payment agent"]);        // (2)
var paymentAgent = new PaymentAgent();                                               // (3)

runtime.Run(BookTheRooms, new Orchestrator(), hotelAgent, paymentAgent);
PrintResult(runtime, gateway, rule);
```

```python
hotels, gateway = Hotels(), PaymentGateway()
rule = ApprovalRule(over_eur=2_000, approver="the travel manager")
runtime = AgentRuntime(hotels, gateway, rule, rule_in=Place.INSTRUCTIONS)   # (1)
hotel_agent = HotelAgent(handoffs=["orchestrator", "Payment agent"])        # (2)
payment_agent = PaymentAgent()                                              # (3)

runtime.run(BOOK_THE_ROOMS, Orchestrator(), hotel_agent, payment_agent)
print_result(runtime, gateway, rule)
```

Both programs print the same output. The naive half:

```text
Naive: write the approval rule in the orchestrator's instructions, and let the agents hand off
  runtime: instructs the orchestrator: "Payments over 2,000 EUR need the travel manager's approval."
  orchestrator hands off to the Hotel agent: "Book 6 rooms at the Garden Inn for 2 nights from March 1."
  Hotel agent asks for BookHotel(Garden Inn, 2027-03-01, 2 nights, 6 rooms)
    hotels: booked B-001 (Garden Inn, 6 rooms, March 1-3, 2,160 EUR)
  Hotel agent hands off to the Payment agent: "B-001 is booked. Pay 2,160 EUR to GARDEN-INN-001."
    runtime: hands control to the Payment agent, past the orchestrator and its instructions, no error
  Payment agent asks for Charge(2,160 EUR, GARDEN-INN-001)
    payment gateway: paid 2,160 EUR to GARDEN-INN-001 (CH-001)
  Payment agent replies to the traveler: "B-001: paid 2,160 EUR to GARDEN-INN-001 (CH-001)."
  Result: 2,160 EUR paid for B-001 (CH-001), and no one was asked to approve it
```

## Where it breaks

**(1) The runtime writes the rule only into the orchestrator's instructions.** Charge runs no rule, and
the orchestrator's model can apply it only to messages that pass through it; the only one, "Book 6
rooms," names no amount.

**(2) The Hotel agent hands off straight to the Payment agent.** Its model picks the Payment agent over
the orchestrator, and nothing in the workflow sends the payment back.

**(3) The Payment agent pays what the handoff names.** A handoff carries the conversation, not the
orchestrator's instructions, and the Payment agent's own instructions carry no approval rule.

The fixed version still lets the Hotel agent hand off past the orchestrator, and the Payment agent still
asks to pay what the handoff names; what changes is that Charge runs the rule for every caller and holds
2,160 EUR for the travel manager's approval.

### How to avoid it

Enforce the rule where money moves. One `ApprovalRule` has two uses: Charge runs it on every call before
the gateway sees the payment, and the runtime writes the orchestrator's instructions from it, as a fact
about Charge that the orchestrator can explain. The models stay as in the naive version, on purpose:
no model sets the limit, decides whether a payment needs approval, or approves one. This is Neward's
answer, applied to agents: the instructions tell the traveler early, as the browser's script
does; Charge is the server's check; and a provider's approval rule, set in its own dashboard, is the
database constraint. OWASP says such checks "must not be delegated to the LLM"
([LLM07](https://genai.owasp.org/llmrisk/llm072025-system-prompt-leakage/)).

```csharp
var (hotels, gateway) = (new Hotels(), new PaymentGateway());
var rule = new ApprovalRule(OverEur: 2_000, Approver: "the travel manager");
var runtime = new AgentRuntime(hotels, gateway, rule, ruleIn: Place.Charge);   // where money moves
var hotelAgent = new HotelAgent(Handoffs: ["orchestrator", "Payment agent"]);
var paymentAgent = new PaymentAgent();

if (!runtime.Run(BookTheRooms, new Orchestrator(), hotelAgent, paymentAgent))
{
    Console.WriteLine("  Result: B-001 booked, nothing paid;"
        + $" no one is set to approve a payment over {rule.OverEur:N0} EUR");
    return;
}

PrintResult(runtime, gateway, rule);
```

```python
hotels, gateway = Hotels(), PaymentGateway()
rule = ApprovalRule(over_eur=2_000, approver="the travel manager")
runtime = AgentRuntime(hotels, gateway, rule, rule_in=Place.CHARGE)   # where money moves
hotel_agent = HotelAgent(handoffs=["orchestrator", "Payment agent"])
payment_agent = PaymentAgent()

if not runtime.run(BOOK_THE_ROOMS, Orchestrator(), hotel_agent, payment_agent):
    print("  Result: B-001 booked, nothing paid;"
          f" no one is set to approve a payment over {rule.over_eur:,} EUR")
    return

print_result(runtime, gateway, rule)
```

And the fixed half of the output:

```text
Fixed: run the approval rule in Charge, and write the instructions from it
  runtime: instructs the orchestrator: "Charge holds payments over 2,000 EUR for the travel manager's approval."
  orchestrator hands off to the Hotel agent: "Book 6 rooms at the Garden Inn for 2 nights from March 1."
  Hotel agent asks for BookHotel(Garden Inn, 2027-03-01, 2 nights, 6 rooms)
    hotels: booked B-001 (Garden Inn, 6 rooms, March 1-3, 2,160 EUR)
  Hotel agent hands off to the Payment agent: "B-001 is booked. Pay 2,160 EUR to GARDEN-INN-001."
    runtime: hands control to the Payment agent, past the orchestrator and its instructions, no error
  Payment agent asks for Charge(2,160 EUR, GARDEN-INN-001)
    runtime: Charge holds 2,160 EUR, over 2,000 EUR, for the travel manager's approval (AR-001)
  Payment agent replies to the traveler: "B-001: held for the travel manager's approval (AR-001)."
  Result: B-001 booked; 2,160 EUR waits for the travel manager's approval (AR-001), nothing paid yet
```

A hold is not a decision. If the travel manager says no, nothing is paid, B-001 stays booked, and the
traveler is told; canceling B-001 is [fallacy 10](../10-system-is-monolithic/). If no one can approve,
Charge refuses, and the runtime never retries through another key or agent. A prompt can refuse, but it
can't hold: without the handoff, the naive orchestrator stops the payment but files no approval request.

In real code, put the rule in the payment service behind Charge, so every caller meets it. In the OpenAI
Agents SDK, `needs_approval` can be a function that decides per call
([OpenAI](https://openai.github.io/openai-agents-python/human_in_the_loop/)); Microsoft Agent
Framework's tool approval, as in [fallacy 4](../04-network-is-secure/), has no threshold, so the limit
goes in the function's body. With Stripe, tag the Payment agent's key as an agent key and add an approval
rule, in public preview, for payment intents: Stripe "returns an approval_required error and
automatically submits an approval request for review"
([Stripe](https://docs.stripe.com/account/approvals)). Check one default too: in
Microsoft.Extensions.AI, a tool that throws reaches the model as "Error: Function failed." The loop then
lets the model try "other function parameters that might succeed," up to three errors in a row
([source](https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs)),
and a smaller amount would. So catch `approval_required` and return the hold as a result; when no one
can approve, set `Terminate` on `FunctionInvokingChatClient.CurrentContext` instead of throwing.

The example leaves out two things. The rule checks each Charge, so a 1,080 EUR deposit and a 1,080 EUR
balance both pass it: make the limit an invariant of the booking, as an aggregate would
([Evans](https://www.domainlanguage.com/wp-content/uploads/2016/05/DDD_Reference_2015-03.pdf)). And the
amount and the account come from the handoff: build Charge from the booking record, as in
[fallacy 4](../04-network-is-secure/).

---

**Read more**

- [The 11th Fallacy of Enterprise Computing](http://web.archive.org/web/20051107004144/http://neward.net/ted/weblog/index.jsp?date=20040517)
  (Ted Neward, 2004, archived)
- [More on the 11th Fallacy](http://web.archive.org/web/20051107004212/http://neward.net/ted/weblog/index.jsp?date=20040518)
  (Ted Neward, 2004, archived)
- [The Fallacies of Enterprise Computing](https://blogs.newardassociates.com/blog/2016/enterprise-computing-fallacies.html)
  (Ted Neward, 2016)
- [Domain-Driven Design Reference](https://www.domainlanguage.com/wp-content/uploads/2016/05/DDD_Reference_2015-03.pdf)
  (Eric Evans, 2015)

[← All ten fallacies](../../README.md)
