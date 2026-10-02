# The example system

Every fallacy in this catalog uses the same system, so you only have to learn it once: a travel assistant
built from an orchestrator and four agents.

| Agent | What it does | Tools |
|---|---|---|
| **Orchestrator** | Takes the user's request, plans, delegates to the other agents, and reviews their proposals. It has no tools of its own. | — |
| **Flight agent** | Searches for and books flights. | `SearchFlights`, `BookFlight`, `CancelFlight` |
| **Hotel agent** | Searches for and books hotels. | `SearchHotels`, `BookHotel`, `CancelHotel` |
| **Payment agent** | Charges the traveler and pays the providers. | `Charge`, `Refund` |
| **Research agent** | Reads web pages and documents: hotel reviews, visa requirements. | `FetchPage` |

A typical request: *"Book me a trip from Madrid to London on March 1, 2027: a flight and two nights in a
hotel near the office. Pay with the company card."* The orchestrator splits it into steps and hands each
one to the agent that owns it.

## Terms

Every entry uses these words the same way.

- **Run**: one user request, handled end to end. The examples call it `trip-42`.
- **Step**: one part of the plan that the orchestrator hands to one agent, such as booking the flight
  (`step-1`).
- **Agent turn**: one pass of an agent over a step, in which its model decides what to do and the tool
  calls it chose are executed.
- **Tool call**: one request from an agent to one of its tools, such as `BookFlight`, which in turn
  reaches an external system such as the airline. The model only asks for it; the runtime executes it.

## In the code

The system is simulated. There are no language models, API keys, or network calls: each agent's
decisions are scripted, and the external systems (the airline, the hotels, the payment gateway) are
small in-memory classes. Every run prints the same output, so you can read it next to the code.

Each example keeps only the parts of the system it needs. Fallacy 1, for instance, uses just the
orchestrator, the Flight agent, and the airline.

[← All ten fallacies](../README.md)
