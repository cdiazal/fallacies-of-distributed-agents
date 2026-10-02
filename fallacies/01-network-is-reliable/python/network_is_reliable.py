"""Fallacy 1: The network is reliable.

Run from the entry folder: python python/network_is_reliable.py   (Python 3.12+, no dependencies)

The orchestrator of a travel assistant asks its Flight agent to book one flight.
The airline books the seat, but the reply to the first call is lost on the way back.
"""

from __future__ import annotations

from collections import deque
from collections.abc import Callable
from dataclasses import dataclass

REQUEST = "Book one flight from Madrid to London on March 1, 2027"


def main() -> None:
    print("Fallacy 1: The network is reliable")
    print()
    naive()
    print()
    fixed()


def naive() -> None:
    """Naive: after a timeout, the orchestrator runs the whole agent turn again."""
    print("Naive: after a timeout, ask the Flight agent again")
    airline = Airline()
    network = Network(lose_reply_on_call=1)
    flight_agent = FlightAgent()

    for attempt in range(1, 4):
        print(f"  attempt {attempt}")
        try:
            call = flight_agent.decide(REQUEST)                                # (1)
            booking = network.call(lambda: airline.book_flight(call.flight))   # (2)
            print(f"    orchestrator: confirmed {booking.flight}")
            break
        except TimeoutError:
            print("    orchestrator: no reply, will retry")                    # (3)

    print_result(airline)


def fixed() -> None:
    """Fixed: the agent decides once; only the tool call is retried, with an idempotency key."""
    print("Fixed: decide once, retry only the tool call, with an idempotency key")
    airline = Airline()
    network = Network(lose_reply_on_call=1)
    flight_agent = FlightAgent()

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

    print_result(airline)


def print_result(airline: Airline) -> None:
    count = len(airline.bookings)
    flights = ", ".join(booking.flight for booking in airline.bookings)
    print(f"  Result: {count} {'booking' if count == 1 else 'bookings'} for 1 request ({flights})")


# ----- The simulated world ------------------------------------------------------------------


class FlightAgent:
    """Stands in for the language model behind the Flight agent. Like a real model, it books nothing:
    it returns the tool call it wants made. Asked again, it doesn't have to ask for the same one.
    """

    def __init__(self) -> None:
        self._scripted_flights = deque(["FL101", "FL207"])

    def decide(self, request: str) -> BookFlightCall:
        return BookFlightCall(self._scripted_flights.popleft())


@dataclass(frozen=True)
class BookFlightCall:
    """A tool call as data: what the model wants done. The runtime decides how to execute it."""

    flight: str


@dataclass(frozen=True)
class Booking:
    id: str
    flight: str


class Airline:
    """The remote system behind the BookFlight tool."""

    def __init__(self) -> None:
        self.bookings: list[Booking] = []
        self._bookings_by_key: dict[str, Booking] = {}

    def book_flight(self, flight: str, idempotency_key: str | None = None) -> Booking:
        if idempotency_key is not None and idempotency_key in self._bookings_by_key:
            existing = self._bookings_by_key[idempotency_key]
            if existing.flight != flight:
                raise ValueError(f"key {idempotency_key} was already used for {existing.flight}")

            print(f"    airline: key {idempotency_key} already used, returns {existing.id}")
            return existing

        booking = Booking(f"B-{len(self.bookings) + 1:03}", flight)
        self.bookings.append(booking)
        if idempotency_key is not None:
            self._bookings_by_key[idempotency_key] = booking

        print(f"    airline: booked {flight} as {booking.id}")
        return booking


class Network:
    """Delivers every request, but loses the reply to one call:
    the remote side has done the work, the caller only sees a timeout.
    """

    def __init__(self, lose_reply_on_call: int) -> None:
        self._lose_reply_on_call = lose_reply_on_call
        self._calls = 0

    def call[T](self, remote_operation: Callable[[], T]) -> T:
        self._calls += 1
        reply = remote_operation()
        if self._calls == self._lose_reply_on_call:
            raise TimeoutError()
        return reply


if __name__ == "__main__":
    main()
