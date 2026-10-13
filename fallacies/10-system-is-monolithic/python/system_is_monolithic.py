"""Fallacy 10: The system is monolithic.

Run from the entry folder: python python/system_is_monolithic.py   (Python 3.12+, no dependencies)

The example system with one change: the orchestrator plans trip-42 once, and the runtime runs the plan
step by step. trip-42 books FL101 and the Garden Inn, then pays the hotel; the company card is declined.
"""

from __future__ import annotations

from dataclasses import dataclass

BOOK_THE_TRIP = ("Book me a trip from Madrid to London on March 1, 2027: a flight and two nights in a hotel"
                 " near the office. Pay with the company card.")


def main() -> None:
    print("Fallacy 10: The system is monolithic")
    print()
    naive()
    print()
    fixed()


def naive() -> None:
    """Naive: the runtime records nothing it could undo, and ends trip-42 on the last reply."""
    print("Naive: run the plan step by step, and end trip-42 on the last reply")
    airline, hotels = Provider.airline(), Provider.hotels()
    gateway = PaymentGateway(card_over_limit=True)
    runtime = AgentRuntime(airline, hotels, gateway, saga=None)                         # (1)
    plan = Orchestrator().plan(BOOK_THE_TRIP)                                           # (2)

    escalated = runtime.run(plan, BookingAgent("Flight agent", "BookFlight"),
                            BookingAgent("Hotel agent", "BookHotel"), PaymentAgent())   # (3)
    print_result(airline, hotels, gateway, escalated)


def fixed() -> None:
    """Fixed: the runtime records an undo as each booking succeeds, and runs them newest first if trip-42 ends unpaid."""
    print("Fixed: record an undo for every booking, and run them newest first if trip-42 ends unpaid")
    airline, hotels = Provider.airline(), Provider.hotels()
    gateway = PaymentGateway(card_over_limit=True)
    saga = Saga(pivot="Charge", undo={"BookFlight": "CancelFlight", "BookHotel": "CancelHotel"})
    runtime = AgentRuntime(airline, hotels, gateway, saga=saga)   # per tool, not per step
    plan = Orchestrator().plan(BOOK_THE_TRIP)

    escalated = runtime.run(plan, BookingAgent("Flight agent", "BookFlight"),
                            BookingAgent("Hotel agent", "BookHotel"), PaymentAgent())
    print_result(airline, hotels, gateway, escalated)


def print_result(airline: Provider, hotels: Provider, gateway: PaymentGateway, escalated: list[str]) -> None:
    held = " and ".join(p.id for p in (airline, hotels) if p.held)
    print("  Result: " + (f"{held} booked, and paid {gateway.paid}" if gateway.paid
          else f"nothing paid; the travel manager has {' and '.join(escalated)}" if escalated
          else f"trip-42 isn't paid, and nothing will cancel {held}" if held else "nothing booked or paid for trip-42"))


# ----- The simulated world ------------------------------------------------------------------

@dataclass(frozen=True)
class Turn:
    """What a model wants next, as data: a tool call or a reply. The runtime carries it out."""
    tool: str | None = None
    argument: str | None = None
    say: str | None = None


@dataclass(frozen=True)
class ToolResult:
    """What a tool call returns: the runtime reads ok and id; the model reads only the text."""
    ok: bool
    text: str
    id: str | None = None


@dataclass(frozen=True)
class Saga:
    """The run's point of no return, and the tool that undoes each other tool: per tool, so no plan has to list them."""
    pivot: str
    undo: dict[str, str]


# An agent: the runtime calls its model with its step's task and the conversation so far.
type Agent = BookingAgent | PaymentAgent


class Orchestrator:
    """The orchestrator's model (no tools): a step each for the flight and the hotel, in the request's order, then one to pay."""

    def plan(self, request: str) -> list[tuple[str, str]]:
        bookings = sorted([(request.find("flight"), "Flight agent", "Book FL101."),
                           (request.find("hotel"), "Hotel agent", "Book Garden Inn, 2027-03-01, 2 nights.")])
        return [(agent, task) for _, agent, task in bookings] + [("Payment agent", "Pay the hotel.")]


@dataclass(frozen=True)
class BookingAgent:
    """The Flight and Hotel agents' model: it asks its tool for what its task names, then replies with what came back."""
    name: str
    tool: str

    def take(self, task: str, conversation: list[str]) -> Turn:
        if conversation[-1] != task:
            return Turn(say=conversation[-1])
        return Turn(tool=self.tool, argument=task.removeprefix("Book ").rstrip("."))


@dataclass(frozen=True)
class PaymentAgent:
    """The Payment agent's model (Charge and Refund only): it pays what the hotel's reply names, then says how it went."""
    name: str = "Payment agent"

    def take(self, task: str, conversation: list[str]) -> Turn:
        reply = next(m for m in reversed(conversation) if "payable to" in m).rstrip(".").split(" ")
        paid = "isn't paid" if "declined" in conversation[-1] else "is paid"
        return (Turn(say=f"{reply[0]} {paid}: {conversation[-1]}.") if conversation[-1] != task
                else Turn(tool="Charge", argument=f"{reply[3]} EUR, {reply[-1]}"))


class AgentRuntime:
    """The agents' runtime: it runs each step's tool calls and ends the run on the last step or the first failed call;
    with a saga, it records an undo for each call that succeeds, and runs them newest first unless the pivot succeeded."""

    def __init__(self, airline: Provider, hotels: Provider, gateway: PaymentGateway, saga: Saga | None) -> None:
        self._tools = {"BookFlight": airline.book, "CancelFlight": airline.cancel, "BookHotel": hotels.book,
                       "CancelHotel": hotels.cancel, "Charge": gateway.charge}
        self._saga, self._undos = saga, []

    def run(self, plan: list[tuple[str, str]], *agents: Agent) -> list[str]:
        """Runs the plan, and returns the bookings whose undo failed, for the travel manager; without a saga, it runs no undo."""
        print("  orchestrator plans trip-42: " + ", ".join(f"step-{i} {a}" for i, (a, _) in enumerate(plan, 1)))
        conversation, pivoted, result = [], False, ToolResult(True, "")
        for step, (name, task) in enumerate(plan, 1):
            agent = next(a for a in agents if a.name == name)
            conversation.append(task)
            while (turn := agent.take(task, conversation)).tool:
                print(f"  {name} asks for {turn.tool}({turn.argument})")
                conversation.append((result := self._tools[turn.tool](turn.argument)).text)
                pivoted |= result.ok and self._saga is not None and turn.tool == self._saga.pivot
                if result.ok and self._saga and (undo_tool := self._saga.undo.get(turn.tool)):
                    self._undos.append((undo_tool, result.id, name))   # from the typed result, never from a reply
                    print(f"    runtime: records {undo_tool}({result.id}) to undo it")
            last = step == len(plan) or not result.ok
            print(f"  {name} replies{' to the traveler' if last else ''}: \"{turn.say}\"")
            conversation.append(turn.say)
            if last:
                break

        print(f"    runtime: ends trip-42 on the {name}'s reply" + (", no error" if self._saga is None and not result.ok else ""))
        if self._saga is None or pivoted:
            return []
        escalated = []
        print(f"  runtime: the log has no successful {self._saga.pivot}; runs trip-42's undos, newest first")
        while self._undos:
            tool, booking, owner = self._undos.pop()
            print(f"  runtime: runs {tool}({booking}) as the {owner}")
            if not self._tools[tool](booking).ok:
                print(f"    runtime: {booking} stays booked; the travel manager is told, and no model retries it")
                escalated.append(booking)
        print("  runtime: tells the traveler: \"Nothing is paid for trip-42"
              + (f"; the travel manager has {' and '.join(escalated)}.\"" if escalated else ", and nothing is booked.\""))
        return escalated


class Provider:
    """The airline or the hotels: each books one thing, and cancels it unless its terms say no."""

    def __init__(self, name: str, booked: str, reply: str, refuses_cancel: str | None) -> None:
        self.held, self.id, self._name, self._booked, self._reply, self._refuses = (
            False, booked.replace(",", " ").split(" ")[0], name, booked, reply, refuses_cancel)

    @staticmethod
    def airline(refuses_cancel: str | None = None) -> Provider:
        return Provider("airline", "FL101, Madrid 07:10 to London 08:35, lands March 1, 2027",
                        "FL101 is booked for March 1.", refuses_cancel)

    @staticmethod
    def hotels(refuses_cancel: str | None = None) -> Provider:
        return Provider("hotels", "B-001 (Garden Inn, March 1-3, 360 EUR)",
                        "B-001 is booked: 360 EUR, payable to GARDEN-INN-001.", refuses_cancel)

    def book(self, argument: str) -> ToolResult:
        self.held = True
        print(f"    {self._name}: booked {self._booked}")
        return ToolResult(True, self._reply, self.id)

    def cancel(self, booking: str) -> ToolResult:
        self.held = self._refuses is not None
        print(f"    {self._name}: " + (f"refused to cancel {booking}: {self._refuses}" if self.held else f"canceled {booking}"))
        return ToolResult(not self.held, f"refused: {self._refuses}" if self.held else f"canceled {booking}")


@dataclass
class PaymentGateway:
    """The payment gateway behind Charge: it charges the company card, and the card's issuer declines it over its limit."""
    card_over_limit: bool
    paid: str | None = None

    def charge(self, argument: str) -> ToolResult:
        self.paid = None if self.card_over_limit else f"{argument.replace(', ', ' to ')} (CH-001)"
        print("    payment gateway: " + (f"declined {argument.split(', ')[0]}: the company card is over its monthly limit"
                                         if self.paid is None else f"paid {self.paid}"))
        return ToolResult(False, "the company card was declined") if self.paid is None else ToolResult(True, self.paid, "CH-001")


if __name__ == "__main__":
    main()
