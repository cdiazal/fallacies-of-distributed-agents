# 10. The system is monolithic

> The system is monolithic — one of Ted Neward's fallacies of enterprise computing.

## The fallacy

In 2016, Ted Neward listed this among his fallacies of enterprise computing
([Neward, 2016](https://blogs.newardassociates.com/blog/2016/enterprise-computing-fallacies.html)).
Systems built in one piece mostly belong to the mainframe years. An enterprise system exists to work with
other systems, and its parts change on their own schedules, so they're deployed, versioned, and often
built separately. Teams then tie the parts together with dependencies nobody wrote down and no plan for
when one fails, and find out in production.

Transactions aren't Neward's point; this entry takes his fallacy one step further. Believing it means
treating work that crosses those parts as one unit that happens whole or not at all. A booking service
reserves a seat with the airline and a room with the hotel, then charges a card, and the card is
declined. Each system has committed its own step, and nothing commits the trip: "Data can't be rolled
back because saga participants commit changes to their respective databases"
([Microsoft](https://learn.microsoft.com/en-us/azure/architecture/patterns/saga)).

The classic answer is a saga
([Garcia-Molina and Salem, 1987](https://www.cs.cornell.edu/andru/cs711/2002fa/reading/sagas.pdf)). The
paper settles for a "(non-atomic) unit": each step gets a compensating transaction that undoes it "from a
semantic point of view," and when a step fails, the steps before it are compensated in reverse order.
Chris Richardson orders the steps: compensatable ones first, then a pivot, the "GO/NO GO point," then
retriable ones
([Richardson, slide 31](https://archive.qconsf.com/system/files/presentation-slides/dataconsistencyinmicroserviceusingsagasqconsf2017-1711151847291.pdf)).

In a classic saga, a developer writes the steps and their compensations in advance, and a failed step
returns an error the coordinator sees.

## The picture

![Left: a booking service reserves a seat with the airline and a room with the hotels, then charges a
card, and the payment gateway declines it; no transaction spans the three systems, so the seat and the
room stay reserved, unpaid; the fix is a saga that undoes finished steps in reverse, puts what can't be
undone last, and calls in a person when an undo fails. Right: the orchestrator plans trip-42 at run time,
with no undo in the plan, and the runtime records nothing it could undo; the Flight agent books FL101 and
the Hotel agent books B-001, but the Payment agent's Charge of 360 EUR to GARDEN-INN-001 is declined, the
Payment agent tells the traveler, and the run ends with no error while FL101 and B-001 stay booked; the
fix has the runtime record each booking's undo as it succeeds and run them newest first when nothing is
paid.](diagram.excalidraw.svg)

## With many agents

A multi-agent run is a series of commits in systems that share no transaction. Where you write the steps,
the classic answer ships: Temporal runs your compensations in reverse and "guarantees that compensations
will execute even after Worker failures"
([Temporal](https://docs.temporal.io/design-patterns/saga-pattern)). Microsoft's Agent Governance
Toolkit, in public preview, has a saga orchestrator
([Microsoft](https://github.com/microsoft/agent-governance-toolkit/blob/main/agent-governance-dotnet/src/AgentGovernance/Hypervisor/SagaOrchestrator.cs)).
Agent frameworks ship save points, not undos: the OpenAI Agents SDK's session-history protection does
"not undo external tool side effects"
([OpenAI](https://openai.github.io/openai-agents-js/guides/sessions/)).

Two things are new. First, no one writes the saga. The orchestrator's model writes the plan at run time,
and each agent's model picks its own calls, so there's no list of steps to attach undos to. An undo can
be tied only to a tool, and recorded only by code that sees each call's typed result; otherwise, the run's
history is text in each agent's conversation. Second, a failed step doesn't fail the run. A saga
coordinator compensates when a step returns an error, but MCP reports "Business logic errors" inside the
tool's result, for the model
([MCP](https://modelcontextprotocol.io/specification/2026-07-28/server/tools)). The Payment agent's model
tells the traveler the truth, and the run ends on that reply like a finished one.

That's why a saga wrapped around agent steps still misses the decline. In the toolkit's orchestrator,
steps are fixed before it runs, and a step counts as committed when it returns without throwing, so the
Payment agent's polite reply commits the saga with two bookings and nothing paid. Telling the
orchestrator to cancel the bookings is no fix: the undo becomes one more improvised plan, built from ids
it read in replies, and a run cut short by an exception or a turn limit never gets that far.

## In code

Both programs simulate the [example system](../../docs/example-system.md) with one change: the
orchestrator plans trip-42 once, and the runtime runs the plan step by step. The Flight agent books
FL101, the Hotel agent books B-001 at the Garden Inn for 360 EUR, and the Payment agent's Charge(360 EUR,
GARDEN-INN-001) is declined: the company card is over its monthly limit. The orchestrator's model plans a
step each for the flight and the hotel, in the request's order, then one to pay; the Flight and Hotel
agents' models ask their tools for what their tasks name and reply with what came back; the Payment
agent's model, with only Charge and Refund, pays what the hotel's reply names and says how it went. Each
program has no dependencies and runs a naive version, then a fixed one. From this folder:

```text
dotnet run dotnet/SystemIsMonolithic.cs     # .NET 10
python python/system_is_monolithic.py       # Python 3.12+
```

The naive runtime records nothing it could undo, and ends the run on the last reply
([full .NET file](dotnet/SystemIsMonolithic.cs), [full Python file](python/system_is_monolithic.py)):

```csharp
var (airline, hotels) = (Provider.Airline(), Provider.Hotels());
var gateway = new PaymentGateway(CardOverLimit: true);
var runtime = new AgentRuntime(airline, hotels, gateway, saga: null);    // (1)
var plan = new Orchestrator().Plan(BookTheTrip);                         // (2)

var escalated = runtime.Run(plan, new BookingAgent("Flight agent", "BookFlight"),
    new BookingAgent("Hotel agent", "BookHotel"), new PaymentAgent());   // (3)
PrintResult(airline, hotels, gateway, escalated);
```

```python
airline, hotels = Provider.airline(), Provider.hotels()
gateway = PaymentGateway(card_over_limit=True)
runtime = AgentRuntime(airline, hotels, gateway, saga=None)                         # (1)
plan = Orchestrator().plan(BOOK_THE_TRIP)                                           # (2)

escalated = runtime.run(plan, BookingAgent("Flight agent", "BookFlight"),
                        BookingAgent("Hotel agent", "BookHotel"), PaymentAgent())   # (3)
print_result(airline, hotels, gateway, escalated)
```

Both programs print the same output. The naive half:

```text
Naive: run the plan step by step, and end trip-42 on the last reply
  orchestrator plans trip-42: step-1 Flight agent, step-2 Hotel agent, step-3 Payment agent
  Flight agent asks for BookFlight(FL101)
    airline: booked FL101, Madrid 07:10 to London 08:35, lands March 1, 2027
  Flight agent replies: "FL101 is booked for March 1."
  Hotel agent asks for BookHotel(Garden Inn, 2027-03-01, 2 nights)
    hotels: booked B-001 (Garden Inn, March 1-3, 360 EUR)
  Hotel agent replies: "B-001 is booked: 360 EUR, payable to GARDEN-INN-001."
  Payment agent asks for Charge(360 EUR, GARDEN-INN-001)
    payment gateway: declined 360 EUR: the company card is over its monthly limit
  Payment agent replies to the traveler: "B-001 isn't paid: the company card was declined."
    runtime: ends trip-42 on the Payment agent's reply, no error
  Result: trip-42 isn't paid, and nothing will cancel FL101 and B-001
```

## Where it breaks

**(1) The runtime records nothing it could undo.** It runs BookFlight and BookHotel and keeps only the
text they return.

**(2) The orchestrator's model improvises the plan, with no undo in it.** It plans the flight and the
hotel, in the request's order, then a payment.

**(3) The Payment agent's model turns the declined card into a reply.** It tells the truth and has no
tool to cancel a booking, and the runtime ends trip-42 on that reply, with no error.

The fixed version still runs the improvised plan, and the Payment agent still reports the decline as a
reply; what changes is that the runtime records CancelFlight(FL101) and CancelHotel(B-001) as each
booking succeeds, and runs them newest first when its log has no successful Charge.

### How to avoid it

Record the undo with the effect. When a booking call succeeds, the runtime records the call that undoes
it, with the id from the typed result, in a log outside every conversation. When the run ends with no
successful or pending Charge, it runs the log newest first, each undo as the agent that owns the tool (as
in [fallacy 6](../06-one-administrator/)), and tells the traveler from the log. The models stay as in the
naive version, on purpose: no model decides whether the run failed, what to undo, or in what order. It's
still a saga, but its steps come from the tools: you write in advance only a fact about each tool. Charge
is the pivot, though Refund undoes it: a pivot may be "the last undoable, or compensable, transaction"
([Microsoft](https://learn.microsoft.com/en-us/azure/architecture/patterns/saga)).

```csharp
var (airline, hotels) = (Provider.Airline(), Provider.Hotels());
var gateway = new PaymentGateway(CardOverLimit: true);
var saga = new Saga(Pivot: "Charge",
    Undo: new() { ["BookFlight"] = "CancelFlight", ["BookHotel"] = "CancelHotel" });
var runtime = new AgentRuntime(airline, hotels, gateway, saga: saga);   // per tool, not per step
var plan = new Orchestrator().Plan(BookTheTrip);

var escalated = runtime.Run(plan, new BookingAgent("Flight agent", "BookFlight"),
    new BookingAgent("Hotel agent", "BookHotel"), new PaymentAgent());
PrintResult(airline, hotels, gateway, escalated);
```

```python
airline, hotels = Provider.airline(), Provider.hotels()
gateway = PaymentGateway(card_over_limit=True)
saga = Saga(pivot="Charge", undo={"BookFlight": "CancelFlight", "BookHotel": "CancelHotel"})
runtime = AgentRuntime(airline, hotels, gateway, saga=saga)   # per tool, not per step
plan = Orchestrator().plan(BOOK_THE_TRIP)

escalated = runtime.run(plan, BookingAgent("Flight agent", "BookFlight"),
                        BookingAgent("Hotel agent", "BookHotel"), PaymentAgent())
print_result(airline, hotels, gateway, escalated)
```

And the fixed half of the output:

```text
Fixed: record an undo for every booking, and run them newest first if trip-42 ends unpaid
  orchestrator plans trip-42: step-1 Flight agent, step-2 Hotel agent, step-3 Payment agent
  Flight agent asks for BookFlight(FL101)
    airline: booked FL101, Madrid 07:10 to London 08:35, lands March 1, 2027
    runtime: records CancelFlight(FL101) to undo it
  Flight agent replies: "FL101 is booked for March 1."
  Hotel agent asks for BookHotel(Garden Inn, 2027-03-01, 2 nights)
    hotels: booked B-001 (Garden Inn, March 1-3, 360 EUR)
    runtime: records CancelHotel(B-001) to undo it
  Hotel agent replies: "B-001 is booked: 360 EUR, payable to GARDEN-INN-001."
  Payment agent asks for Charge(360 EUR, GARDEN-INN-001)
    payment gateway: declined 360 EUR: the company card is over its monthly limit
  Payment agent replies to the traveler: "B-001 isn't paid: the company card was declined."
    runtime: ends trip-42 on the Payment agent's reply
  runtime: the log has no successful Charge; runs trip-42's undos, newest first
  runtime: runs CancelHotel(B-001) as the Hotel agent
    hotels: canceled B-001
  runtime: runs CancelFlight(FL101) as the Flight agent
    airline: canceled FL101
  runtime: tells the traveler: "Nothing is paid for trip-42, and nothing is booked."
  Result: nothing booked or paid for trip-42
```

An undo can fail too. The 1987 paper asked what happens when a compensation can't complete, and Mark
Richards and Neal Ford later named "compensating updates always work" a fallacy
([Lesson 148](https://www.developertoarchitect.com/lessons/lesson148.html)). If the hotels refuse
CancelHotel(B-001), the runtime still cancels FL101, hands B-001 to the travel manager, and asks no model
for another way. A real airline may cancel and keep the fare: count an undo as done only when its result
says so. And have the runtime keep what can't be undone last, after "all critical validations succeed"
([Microsoft](https://learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction)).

In real code, record the undo where the result is still typed, in Microsoft Agent Framework's function
middleware after the call returns
([Microsoft](https://learn.microsoft.com/en-us/agent-framework/concepts/agents/middleware/)); the OpenAI
Agents SDK's [`on_tool_end`](https://openai.github.io/openai-agents-python/tools/#handling-errors-in-function-tools)
also fires when a tool raises: check its result. Decide from the log, however the run ends,
`MaxTurnsExceeded` and crashes included. Check one default: Microsoft Agent Framework for .NET makes runs
atomic, meaning "chat history is only updated when the full run succeeds"
([ADR 0022](https://github.com/microsoft/agent-framework/blob/main/docs/decisions/0022-chat-history-persistence-consistency.md)).
If the run throws after BookFlight succeeded, the session keeps no trace of FL101: atomic for the chat
history, not for the airline. Keep the log in your own durable store, written per call.

The example leaves out a lost reply: record the intent before each call, keyed by run and step as in
[fallacy 1](../01-network-is-reliable/), and treat a Charge with no reply as pending until its outcome is
known. The same log undoes finished steps in [fallacy 5](../05-topology-doesnt-change/), FL101 in
[fallacy 8](../08-network-is-homogeneous/), and B-001 in [fallacy 9](../09-logic-can-be-centralized/)
once the travel manager says no, even days later.

---

**Read more**

- [The Fallacies of Enterprise Computing](https://blogs.newardassociates.com/blog/2016/enterprise-computing-fallacies.html)
  (Ted Neward, 2016)
- [Sagas](https://www.cs.cornell.edu/andru/cs711/2002fa/reading/sagas.pdf)
  (Hector Garcia-Molina and Kenneth Salem, 1987)
- [Compensating Transaction pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction)
  (Microsoft)
- [The Fallacies of Compensating Updates](https://www.developertoarchitect.com/lessons/lesson148.html)
  (Mark Richards, 2022)
- Related: [Robust Agent Compensation (RAC): Teaching AI Agents to Compensate](https://arxiv.org/abs/2605.03409)
  (Srinath Perera, Kaviru Hapuarachchi, Frank Leymann, and Rania Khalaf, 2026), which proposes "a log-based
  recovery paradigm" for agents, as this fix does

[← All ten fallacies](../../README.md)
