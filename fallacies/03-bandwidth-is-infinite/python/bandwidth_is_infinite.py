"""Fallacy 3: Bandwidth is infinite.

Run from the entry folder: python python/bandwidth_is_infinite.py   (Python 3.12+, no dependencies)

The Research agent has read every review of the hotels near the office, and the orchestrator now asks
the Hotel agent to book one. At the start of the run, the user set a budget: 200 EUR a night.
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum

USER_BUDGET = 200             # EUR a night
HOTEL_AGENT_WINDOW = 128_000  # tokens the Hotel agent's model can handle in one call


def main() -> None:
    print("Fallacy 3: Bandwidth is infinite")
    print()
    naive()
    print()
    fixed()


def naive() -> None:
    """Naive: hand over everything, and let the runtime cut it down to fit."""
    print("Naive: forward every review and the whole run history")
    research_agent = ResearchAgent()

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

    print_result(call)


def fixed() -> None:
    """Fixed: references instead of payloads, constraints inside the task, and overflow as an error."""
    print("Fixed: a summary and a reference, with the budget in the task")
    research_agent = ResearchAgent()
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


def print_result(call: BookHotelCall) -> None:
    verdict = (f"within the {USER_BUDGET} EUR budget" if call.price_per_night <= USER_BUDGET
               else f"over the {USER_BUDGET} EUR budget the user set at the start")
    print(f"  Result: {call.price_per_night} EUR a night, {verdict}")


# ----- The simulated world ------------------------------------------------------------------


@dataclass(frozen=True)
class Message:
    """A message as the model sees it: the budget and the hotels its text carries, if any."""
    text: str
    tokens: int
    budget: int | None = None
    hotels: tuple[Hotel, ...] = ()
    reference: str | None = None


@dataclass(frozen=True)
class BookHotelCall:
    """A tool call as data: what the model wants done."""
    hotel: str
    price_per_night: int


@dataclass(frozen=True)
class Hotel:
    name: str
    price_per_night: int
    rating: float


class ResearchAgent:
    """Stands in for the Research agent: it has read 125,000 tokens of reviews."""

    TOP_THREE = (
        Hotel("Regent", 340, 4.8),
        Hotel("Garden Inn", 180, 4.5),
        Hotel("Station Hotel", 150, 3.9),
    )

    def return_every_review(self) -> Message:
        return Message("Research agent: every review of 40 hotels", tokens=125_000, hotels=self.TOP_THREE)

    def store_reviews_and_summarize(self, store: dict[str, Message], max_tokens: int) -> Message:
        reference = "reviews/trip-42"
        store[reference] = self.return_every_review()
        summary = Message(f"Research agent: top 3 near the office, with prices and ratings. Full reviews: {reference}",
                          tokens=120, hotels=self.TOP_THREE, reference=reference)
        if summary.tokens > max_tokens:
            raise ValueError("summary over its size limit")
        return summary


class Overflow(Enum):
    DROP_OLDEST = "drop oldest"
    FAIL = "fail"


class ContextTooLargeError(Exception):
    pass


class HotelAgent:
    """Stands in for the Hotel agent: a runtime with a fixed context window, and a model that books the
    best-rated hotel it can see, within the budget if the budget is still in its window."""

    def __init__(self, window: int, on_overflow: Overflow) -> None:
        self._window = window
        self._on_overflow = on_overflow

    def decide(self, messages: list[Message]) -> BookHotelCall:
        total = sum(m.tokens for m in messages)
        print(f"  Hotel agent receives {len(messages)} messages: {total:,} tokens for a {self._window:,}-token window")

        if total > self._window and self._on_overflow is Overflow.FAIL:
            print("    runtime: refused the task with an error, nothing dropped")
            raise ContextTooLargeError(f"the task is {total:,} tokens for a {self._window:,}-token window")

        visible = list(messages)
        while sum(m.tokens for m in visible) > self._window:
            print(f"    runtime: dropped the oldest message, no error: \"{visible[0].text}\"")
            visible.pop(0)

        budget = min((m.budget for m in visible if m.budget is not None), default=None)
        hotels = [h for m in visible for h in m.hotels if budget is None or h.price_per_night <= budget]
        if not hotels:
            raise RuntimeError("no hotel left in the window")
        hotel = max(hotels, key=lambda h: h.rating)
        print(f"  Hotel agent asks for BookHotel({hotel.name}, {hotel.price_per_night} EUR a night)")
        return BookHotelCall(hotel.name, hotel.price_per_night)


if __name__ == "__main__":
    main()
