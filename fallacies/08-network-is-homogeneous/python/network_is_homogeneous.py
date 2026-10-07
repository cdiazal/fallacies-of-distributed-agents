"""Fallacy 8: The network is homogeneous.

Run from the entry folder: python python/network_is_homogeneous.py   (Python 3.12+, no dependencies)

The example system with one change: two providers serve the Flight agent's model, and the runtime sends
its calls to the cheaper one. Both accept its reply schema; only provider A enforces "pattern".
"""

from __future__ import annotations

import re
from collections.abc import Callable
from dataclasses import dataclass
from datetime import date, datetime, timedelta

BOOK_THE_FLIGHT = "step-1: book FL101 for trip-42"
MONTHS = ("January", "February", "March", "April", "May", "June", "July",   # not the machine's locale
          "August", "September", "October", "November", "December")


def main() -> None:
    print("Fallacy 8: The network is homogeneous")
    print()
    naive()
    print()
    fixed()


def naive() -> None:
    """Naive: the runtime passes on the Flight agent's reply as the provider returns it."""
    print("Naive: route the Flight agent's model to the cheaper provider, and pass on what it replies")
    airline, hotels = Airline(), Hotels()
    runtime = AgentRuntime(airline, hotels, contract=None)               # (1)
    by_price = [Provider("provider B", enforces=("type", "required")),   # (2)
                Provider("provider A", enforces=("type", "required", "pattern"))]
    hotel_agent = HotelAgent()                                           # (3)

    if (arrival := runtime.run_flight_step(FlightAgent(), by_price, BOOK_THE_FLIGHT)) is not None:
        runtime.run_hotel_step(hotel_agent, arrival)
    print_result(airline, hotels)


def fixed() -> None:
    """Fixed: the runtime parses every reply itself, and asks the next provider when one fails."""
    print("Fixed: parse every reply on our side, and ask the next provider when one fails")
    airline, hotels = Airline(), Hotels()
    runtime = AgentRuntime(airline, hotels, contract=ReplyContract())   # on our side
    by_price = [Provider("provider B", enforces=("type", "required")),
                Provider("provider A", enforces=("type", "required", "pattern"))]
    hotel_agent = HotelAgent()

    if (arrival := runtime.run_flight_step(FlightAgent(), by_price, BOOK_THE_FLIGHT)) is None:
        print("  Result: FL101 booked, but no hotel for trip-42;"
              " no provider's reply met the Flight agent's contract")
        return

    runtime.run_hotel_step(hotel_agent, arrival)
    print_result(airline, hotels)


def print_result(airline: Airline, hotels: Hotels) -> None:
    print(f"  Result: {airline.booked.flight} lands on {long_date(airline.booked.lands)},"
          f" and the Garden Inn is booked for {Hotels.stay(hotels.check_in, 2)} (B-001)")


# ----- The simulated world ------------------------------------------------------------------

@dataclass(frozen=True)
class ToolCall:
    """A tool call as data: what the model wants done."""
    tool: str
    argument: str


@dataclass(frozen=True)
class Confirmation:
    """What the airline confirms, typed; the runtime hands it to the Flight agent's model as the tool result."""
    flight: str
    route: str
    lands: date


@dataclass(frozen=True)
class Reply:
    """The Flight agent's reply, as its schema asks for it. JSON has no date type, so the arrival is text."""
    flight: str
    arrival: str

    ARRIVAL_PATTERN = "^[0-9]{4}-[0-9]{2}-[0-9]{2}$"   # the schema's "pattern"


class FlightAgent:
    """The Flight agent's model: book the step's flight, then reply with its landing day, day first unless forced."""

    def decide(self, step: str) -> ToolCall:
        return ToolCall("BookFlight", step.split(" ")[2])

    def answer(self, booked: Confirmation, allowed: Callable[[str], bool]) -> Reply:
        day_first = f"{booked.lands.day:02}/{booked.lands.month:02}/{booked.lands.year}"
        return Reply(booked.flight, day_first if allowed(day_first) else booked.lands.isoformat())


class HotelAgent:
    """The Hotel agent's model: book 2 nights from the task's date, reading slashes month first when it can."""

    def decide(self, task: str) -> ToolCall:
        text = task.split(" from ")[-1].rstrip(".")
        if len(parts := text.split("/")) == 3:
            first, second, year = (int(part) for part in parts)
            text = (date(year, first, second) if first <= 12 else date(year, second, first)).isoformat()
        return ToolCall("BookHotel", f"Garden Inn, {text}, 2 nights")


@dataclass(frozen=True)
class Provider:
    """A provider serving the Flight agent's model: it enforces the schema keywords it supports, and ignores the rest."""
    name: str
    enforces: tuple[str, ...]

    def generate(self, model: Callable[[Callable[[str], bool]], Reply]) -> Reply:
        return model(lambda text: "pattern" not in self.enforces or re.match(Reply.ARRIVAL_PATTERN, text) is not None)

    @property
    def did(self) -> str:
        return ('accepted the schema and enforced its "pattern"' if "pattern" in self.enforces
                else 'accepted the schema but ignored its "pattern", no error')


class ReplyContract:
    """Ours, not the provider's: the reply's arrival must be a real date, written YYYY-MM-DD."""

    def parse(self, reply: Reply) -> date | None:
        try:
            return datetime.strptime(reply.arrival, "%Y-%m-%d").date() if re.match(Reply.ARRIVAL_PATTERN, reply.arrival) else None
        except ValueError:
            return None


class AgentRuntime:
    """The agents' runtime: it routes model calls, runs tool calls, and fills step-2's date from the Flight agent's reply."""

    def __init__(self, airline: Airline, hotels: Hotels, contract: ReplyContract | None) -> None:
        self._airline, self._hotels, self._contract = airline, hotels, contract

    def run_flight_step(self, agent: FlightAgent, by_price: list[Provider], step: str) -> str | None:
        print(f"  runtime: sends the Flight agent's model calls to {by_price[0].name}, the cheaper of the two")
        call = agent.decide(step)
        print(f"  Flight agent asks for {call.tool}({call.argument})")
        booked = self._airline.book(call)
        for i, provider in enumerate(by_price):
            reply = provider.generate(lambda allowed: agent.answer(booked, allowed))
            print(f'  Flight agent replies through {provider.name}: {{"flight": "{reply.flight}", "arrival": "{reply.arrival}"}}')
            print(f"    {provider.name}: {provider.did}")
            if self._contract is None:
                return reply.arrival
            if (arrival := self._contract.parse(reply)) is not None:
                print(f"    runtime: parsed arrival as {long_date(arrival)}")
                return arrival.isoformat()

            then = f"asks {by_price[i + 1].name} for the reply" if i + 1 < len(by_price) else "no provider left"
            print(f"    runtime: refused the reply: arrival \"{reply.arrival}\" isn't a YYYY-MM-DD date; {then}")
        return None

    def run_hotel_step(self, agent: HotelAgent, arrival: str) -> None:
        task = f"Book the Garden Inn for 2 nights from {arrival}."
        print(f'  runtime: hands step-2 to the Hotel agent: "{task}"')
        call = agent.decide(task)
        print(f"  Hotel agent asks for {call.tool}({call.argument})")
        self._hotels.book(call)


class Airline:
    """The airline behind BookFlight. It confirms each booking with a typed date."""

    def __init__(self) -> None:
        self.booked: Confirmation | None = None

    def book(self, call: ToolCall) -> Confirmation:
        self.booked = Confirmation(call.argument, "Madrid 07:10 to London 08:35", date(2027, 3, 1))
        print(f"    airline: booked {self.booked.flight}, {self.booked.route}, lands {long_date(self.booked.lands)}")
        return self.booked


class Hotels:
    """The hotels behind BookHotel. A booking needs a real check-in date, written YYYY-MM-DD."""

    def __init__(self) -> None:
        self.check_in: date | None = None

    def book(self, call: ToolCall) -> None:
        name, check_in, nights = call.argument.split(", ")
        self.check_in = datetime.strptime(check_in, "%Y-%m-%d").date()
        print(f"    hotels: booked B-001 ({name}, {Hotels.stay(self.check_in, int(nights[0]))}, {180 * int(nights[0])} EUR)")

    @staticmethod
    def stay(check_in: date, nights: int) -> str:
        """How the trace writes a stay: "March 1-3", or "January 31-February 2" across months."""
        check_out = check_in + timedelta(days=nights)
        end = (str(check_out.day) if check_out.month == check_in.month
               else f"{MONTHS[check_out.month - 1]} {check_out.day}")
        return f"{MONTHS[check_in.month - 1]} {check_in.day}-{end}"


def long_date(day: date) -> str:
    """How the trace writes a date: "March 1, 2027", with the month names above."""
    return f"{MONTHS[day.month - 1]} {day.day}, {day.year}"


if __name__ == "__main__":
    main()
