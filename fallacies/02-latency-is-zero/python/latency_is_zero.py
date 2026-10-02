"""Fallacy 2: Latency is zero.

Run from the entry folder: python python/latency_is_zero.py   (Python 3.12+, no dependencies)

To plan a trip, the orchestrator needs a flight, a hotel, the hotel's reviews, and the visa rules.
The user's app gives up after 20 seconds. Time is simulated, so the program runs instantly.
"""

from __future__ import annotations

from dataclasses import dataclass

USER_DEADLINE = 20   # seconds


def main() -> None:
    print("Fallacy 2: Latency is zero")
    print()
    naive()
    print()
    fixed()


def naive() -> None:
    """Naive: one agent at a time, one small question at a time, and the deadline stays with the user."""
    print("Naive: one agent at a time, one small question at a time")
    timeline = Timeline(USER_DEADLINE)
    flight_agent = Agent("Flight agent", seconds_per_turn=4)
    hotel_agent = Agent("Hotel agent", seconds_per_turn=4)
    research_agent = Agent("Research agent", seconds_per_turn=6)

    now = 0
    now = timeline.ask(flight_agent, "search flights to London", start_at=now)      # (1)
    now = timeline.ask(flight_agent, "pick the morning flight", start_at=now)       # (2)
    now = timeline.ask(hotel_agent, "search hotels near the office", start_at=now)
    now = timeline.ask(hotel_agent, "pick the one with breakfast", start_at=now)
    now = timeline.ask(research_agent, "check the hotel's reviews", start_at=now)   # (3)
    now = timeline.ask(research_agent, "check the visa rules", start_at=now)

    print_result(plan_ready_at=now, timeline=timeline)


def fixed() -> None:
    """Fixed: one message per task, independent tasks at the same time, and the deadline passed down."""
    print("Fixed: one message per task, in parallel when independent, deadline passed down")
    timeline = Timeline(USER_DEADLINE)
    flight_agent = Agent("Flight agent", seconds_per_turn=4)
    hotel_agent = Agent("Hotel agent", seconds_per_turn=4)
    research_agent = Agent("Research agent", seconds_per_turn=6)

    # The flight, the hotel, and the visa rules don't depend on each other: ask for them together.
    flight = timeline.ask(flight_agent, "find the morning flight to London",
                          start_at=0, deadline=USER_DEADLINE)
    hotel = timeline.ask(hotel_agent, "find a hotel near the office, with breakfast",
                         start_at=0, deadline=USER_DEADLINE)
    visa = timeline.ask(research_agent, "check the visa rules",
                        start_at=0, deadline=USER_DEADLINE)
    # The reviews need the hotel, so they start when it's chosen.
    reviews = timeline.ask(research_agent, "check the hotel's reviews",
                           start_at=hotel, deadline=USER_DEADLINE)

    print_result(plan_ready_at=max(flight, hotel, visa, reviews), timeline=timeline)


def print_result(plan_ready_at: int, timeline: Timeline) -> None:
    if timeline.canceled:
        print(f"  Result: partial plan at {USER_DEADLINE} s; missing: {'; '.join(timeline.canceled)}")
    elif plan_ready_at <= USER_DEADLINE:
        print(f"  Result: plan ready at {plan_ready_at} s, within the {USER_DEADLINE} s deadline")
    else:
        print(f"  Result: plan ready at {plan_ready_at} s, but the user gave up at {USER_DEADLINE} s;"
              f" {timeline.turns_after_user_left} agent turns ended after that")


# ----- The simulated world ------------------------------------------------------------------


@dataclass(frozen=True)
class Agent:
    """An agent, reduced to what matters here: a model turn takes seconds, not milliseconds."""

    name: str
    seconds_per_turn: int


class Timeline:
    """Stands in for the agents' models and the clock. Nothing waits: ask puts a turn on the timeline and
    returns the second it ends, which is when anything that needs its answer can start.
    """

    def __init__(self, user_deadline: int) -> None:
        self._user_deadline = user_deadline
        self.turns_after_user_left = 0
        self.canceled: list[str] = []

    def ask(self, agent: Agent, task: str, start_at: int, deadline: int | None = None) -> int:
        end_at = start_at + agent.seconds_per_turn
        if deadline is not None and end_at > deadline:
            self.canceled.append(task)
            print(f"  {start_at:>2} s -> {deadline:>2} s  {agent.name}: {task}   <- canceled at the deadline")
            return deadline

        after_user_left = end_at > self._user_deadline
        if after_user_left:
            self.turns_after_user_left += 1

        note = "   <- the user has gone" if after_user_left else ""
        print(f"  {start_at:>2} s -> {end_at:>2} s  {agent.name}: {task}{note}")
        return end_at


if __name__ == "__main__":
    main()
