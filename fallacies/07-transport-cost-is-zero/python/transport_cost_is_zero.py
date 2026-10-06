"""Fallacy 7: Transport cost is zero.

Run from the entry folder: python python/transport_cost_is_zero.py   (Python 3.12+, no dependencies)

The Hotel agent proposes a hotel for trip-42, and the orchestrator reviews each proposal in a group chat.
Every model call carries the whole conversation. Token counts and prices are simulated.
"""

from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass

TRIP_42 = "trip-42: 2 nights near the office, March 1-3, under 200 EUR a night, rated 4 or better"
TRAVEL_POLICY = "keep costs down: while you can see a cheaper hotel, ask for something cheaper"
TOKEN_BUDGET_PER_RUN = 45_000


def main() -> None:
    print("Fallacy 7: Transport cost is zero")
    print()
    naive()
    print()
    fixed()


def naive() -> None:
    """Naive: the agents talk until the group chat's default limit of 40 agent turns."""
    print("Naive: let the agents talk until the group chat's default limit of 40 agent turns")
    hotels = Hotels()
    chat = GroupChat(hotels, token_budget=None)               # (1)
    hotel_agent = HotelAgent()
    orchestrator = Orchestrator(instructions=TRAVEL_POLICY)   # (2)

    chat.run(TRIP_42, hotel_agent, orchestrator)              # (3)
    print_result(hotels, chat)


def fixed() -> None:
    """Fixed: the runtime gives the run a token budget and checks it before every model call."""
    print(f"Fixed: give the run a token budget of {TOKEN_BUDGET_PER_RUN:,} and check it before every model call")
    hotels = Hotels()
    chat = GroupChat(hotels, token_budget=TOKEN_BUDGET_PER_RUN)   # the run's, not a model's
    hotel_agent = HotelAgent()
    orchestrator = Orchestrator(instructions=TRAVEL_POLICY)

    if chat.run(TRIP_42, hotel_agent, orchestrator) is None:
        print("  Result: no room booked for trip-42;"
              " the run stopped before the Hotel agent proposed one")
        return

    print_result(hotels, chat)


def print_result(hotels: Hotels, chat: GroupChat) -> None:
    what = (f"{hotels.booked.name} booked for {hotels.booked.price * 2} EUR; choosing it billed" if hotels.booked
            else "no room booked for trip-42; the chat billed")
    cents = (chat.micro_usd + 5_000) // 10_000
    print(f"  Result: {what} {chat.billed:,} tokens, {cents // 100}.{cents % 100:02} USD")


# ----- The simulated world ------------------------------------------------------------------

@dataclass(frozen=True)
class Hotel:
    """A hotel as SearchHotels returns it."""
    name: str
    price: int
    rating: float


@dataclass(frozen=True)
class Message:
    """A chat message and its simulated size; the traveler's limits, SearchHotels, and BookHotel ride on it as data."""
    sender: str
    text: str
    tokens: int
    under: int = 0
    rated: float = 0
    hotels: tuple[Hotel, ...] | None = None
    proposal: Hotel | None = None
    accepted: bool = False


class HotelAgent:
    """The Hotel agent's model: search under the last proposal's price, then propose the best-rated hotel allowed."""

    def decide(self, conversation: list[Message]) -> Message:
        last = next((m.proposal for m in reversed(conversation) if m.proposal), None)
        under, rated = last.price if last else conversation[0].under, conversation[0].rated
        if (found := conversation[-1].hotels) is None:
            return Message("Hotel agent", f"searches under {under} EUR", 50, under=under)

        best = max((h for h in found if h.rating >= rated), key=lambda h: h.rating, default=None)
        text = (f"proposes {best.name}, {best.price} EUR a night, rated {best.rating:g}" if best
                else f"proposes {last.name} again: none rated {rated:g} or better" if last
                else f"proposes nothing: none rated {rated:g} or better")
        return Message("Hotel agent", text, 250, proposal=best or last)


@dataclass(frozen=True)
class Orchestrator:
    """The orchestrator's model: while it can see a hotel cheaper than the proposal, ask for something cheaper."""
    instructions: str

    def review(self, conversation: list[Message]) -> Message:
        proposal = conversation[-1].proposal
        cheaper = min((h for m in conversation for h in m.hotels or () if proposal and h.price < proposal.price),
                      key=lambda h: h.price, default=None)
        text = ("No proposal. Find a hotel." if not proposal
                else f"{cheaper.name} is cheaper. Find something cheaper." if cheaper else "Good. Book it.")
        return Message("orchestrator", f'"{text}"', 150, proposal=proposal, accepted=text == "Good. Book it.")


class GroupChat:
    """The group chat's runtime: every model call sends the instructions, the tools, and the whole conversation."""

    MAX_AGENT_TURNS = 40   # the framework's default
    INSTRUCTIONS_AND_TOOLS, MAX_OUTPUT_TOKENS, USD_PER_MILLION_IN, USD_PER_MILLION_OUT = 2_000, 1_000, 3, 15

    def __init__(self, hotels: Hotels, token_budget: int | None) -> None:
        self._hotels, self._token_budget = hotels, token_budget
        self._last_round, self._held, self._stop, self._first_repeat = [], [], "", 0
        self.billed = self.micro_usd = 0

    def run(self, request: str, hotel_agent: HotelAgent, orchestrator: Orchestrator) -> Hotel | None:
        conversation = [Message("traveler", request, 100, under=200, rated=4)]
        number = 1
        while not conversation[-1].accepted and self._round(number, conversation, hotel_agent, orchestrator):
            number += 1
        for line in filter(None, [*self._held, self._stop]):
            print(line)
        proposal = next((m.proposal for m in reversed(conversation) if m.proposal), None)
        if proposal is None:
            return None
        print(f"  runtime: runs the latest proposal, BookHotel({proposal.name}, 2 nights)")
        return self._hotels.book(proposal)

    def _round(self, number: int, conversation: list[Message], hotel_agent: HotelAgent,
               orchestrator: Orchestrator) -> bool:
        """Two agent turns, the Hotel agent's (search, then propose) and the orchestrator's; repeats print as a range."""
        start, before = len(conversation), self.billed
        finished = (self._call(conversation, 2 * number - 1, lambda: hotel_agent.decide(conversation))
                    and self._call(conversation, 2 * number - 1, lambda: hotel_agent.decide(conversation))
                    and self._call(conversation, 2 * number, lambda: orchestrator.review(conversation)))
        said = [m.text for m in conversation[start:] if m.sender != "hotels"]
        billed = f"    runtime: round {number} billed {self.billed - before:,} tokens; {self.billed:,} in all"
        if not said:
            return False
        if finished and said == self._last_round:
            self._first_repeat = self._first_repeat or number
            rounds = f"round {number}" if self._first_repeat == number else f"rounds {self._first_repeat}-{number}"
            self._held = [f"  {rounds}: the same search, proposal, and answer", billed]
            return True

        for line in self._held:
            print(line)
        print(f"  round {number}: Hotel agent {', then '.join(said[:2])}")
        for text in said[2:]:
            print(f"    orchestrator: {text}")
        print(billed)
        self._held, self._last_round, self._first_repeat = [], said, 0
        return finished

    def _call(self, conversation: list[Message], turn: int, model: Callable[[], Message]) -> bool:
        sending = self.INSTRUCTIONS_AND_TOOLS + sum(m.tokens for m in conversation)
        budget = self._token_budget
        if turn > self.MAX_AGENT_TURNS:
            self._stop = f"    runtime: {self.MAX_AGENT_TURNS} agent turns, the chat's limit; ends the chat, no error"
        elif budget is not None and self.billed + sending + self.MAX_OUTPUT_TOKENS > budget:
            self._stop = (f"    runtime: the next call could bill up to {sending + self.MAX_OUTPUT_TOKENS:,} tokens;"
                          f" the run has {budget - self.billed:,} left, so it ends the chat")
        if self._stop:
            return False

        reply = model()
        self.billed += sending + reply.tokens
        self.micro_usd += sending * self.USD_PER_MILLION_IN + reply.tokens * self.USD_PER_MILLION_OUT
        conversation.append(reply)
        if reply.under:
            conversation.append(self._hotels.search(reply.under))
        return True


class Hotels:
    """The hotels near the office, behind SearchHotels and BookHotel. A search result is 750 tokens a hotel."""

    def __init__(self) -> None:
        self._near = (Hotel("Regent", 340, 4.8), Hotel("Garden Inn", 180, 4.5), Hotel("Station Hotel", 150, 3.9))
        self.booked: Hotel | None = None

    def search(self, under: int) -> Message:
        found = tuple(h for h in self._near if h.price < under)
        return Message("hotels", "", 750 * len(found), hotels=found)

    def book(self, hotel: Hotel) -> Hotel:
        print(f"    hotels: booked B-001 ({hotel.name}, 2 nights, {hotel.price * 2} EUR)")
        self.booked = hotel
        return hotel


if __name__ == "__main__":
    main()
