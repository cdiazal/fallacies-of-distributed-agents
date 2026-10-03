"""Fallacy 5: Topology doesn't change.

Run from the entry folder: python python/topology_doesnt_change.py   (Python 3.12+, no dependencies)

At turn 1, the orchestrator plans the hotel step of trip-42 with the Hotel agent's tools. Before the step
runs, the hotels' MCP server is redeployed: HoldHotel and ConfirmHotel replace BookHotel.
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum


def main() -> None:
    print("Fallacy 5: Topology doesn't change")
    print()
    naive()
    print()
    fixed()


def naive() -> None:
    """Naive: plan once, at turn 1, and run the plan as written."""
    print("Naive: run the plan from turn 1 as written")
    hotels = HotelsServer()
    hotel_agent = HotelAgent(hotels)

    orchestrator = Orchestrator(hotel_agent, on_tools_changed=ToolsChanged.KEEP_PLAN)   # (1)
    step = orchestrator.plan("at turn 1")                                               # (2)
    hotels.redeploy(HotelsServer.HOLD_THEN_CONFIRM, notify=hotel_agent)
    orchestrator.run(step)                                                              # (3)

    print_result(hotels)


def fixed() -> None:
    """Fixed: before each step runs, the runtime checks its tools against the agent's current ones."""
    print("Fixed: before each step, check its tools against the agent's current ones")
    hotels = HotelsServer()
    hotel_agent = HotelAgent(hotels)

    orchestrator = Orchestrator(hotel_agent, on_tools_changed=ToolsChanged.PLAN_AGAIN)
    try:
        step = orchestrator.plan("at turn 1")   # the step keeps full tool definitions
        hotels.redeploy(HotelsServer.HOLD_THEN_CONFIRM, notify=hotel_agent)
        orchestrator.run(step)
        print_result(hotels)
    except NoPlanError:
        print("  Result: no room booked yet; tell the traveler")


def print_result(hotels: HotelsServer) -> None:
    lapses = "".join(f"; hold {hold} lapses in 15 minutes" for hold in hotels.holds)
    print(f"  Result: 1 room booked for trip-42: {hotels.bookings[0]}" if hotels.bookings
          else f"  Result: no room booked for trip-42{lapses}")


# ----- The simulated world ------------------------------------------------------------------


@dataclass(frozen=True)
class Tool:
    """A tool definition as the server lists it. Dataclasses compare by value, so a tool changed in place counts as changed."""
    name: str
    takes: str
    returns: str


@dataclass(frozen=True)
class Step:
    """One step of the plan, with the full tool definitions it was planned with, in the order to call them."""
    id: str
    stay: str
    tools: tuple[Tool, ...]


@dataclass(frozen=True)
class ToolCall:
    """A tool call as data: what the model wants done."""
    tool: str
    argument: str


class ToolsChanged(Enum):
    KEEP_PLAN = "keep plan"
    PLAN_AGAIN = "plan again"


class NoPlanError(Exception):
    pass


class Orchestrator:
    """Stands in for the orchestrator: a model that plans a step as a chain of the agent's tools, in list order,
    from the stay toward a booking, and a runtime that runs each step (with PLAN_AGAIN, only if complete and current)."""

    STAY = "Garden Inn, 2 nights"

    def __init__(self, hotel_agent: HotelAgent, on_tools_changed: ToolsChanged) -> None:
        self._hotel_agent = hotel_agent
        self._on_tools_changed = on_tools_changed

    def plan(self, when: str) -> Step:
        print(f"  orchestrator plans {when}, with the Hotel agent's tools: {', '.join(t.name for t in self._hotel_agent.tools)}")
        chain: list[Tool] = []
        have = "stay"
        for tool in self._hotel_agent.tools:
            if have != "booking" and tool.takes == have:
                chain.append(tool)
                have = tool.returns

        if have != "booking" and self._on_tools_changed is ToolsChanged.PLAN_AGAIN:
            print("    orchestrator runtime: no plan for step-2 with the Hotel agent's tools; nothing called")
            raise NoPlanError("step-2")

        calls = [f"{t.name}({self.STAY if i == 0 else chain[i - 1].returns})" for i, t in enumerate(chain)]
        print(f"    step-2: Hotel agent, {', then '.join(calls)}")
        return Step("step-2", self.STAY, tuple(chain))

    def run(self, step: Step) -> None:
        gone = next((t for t in step.tools if t not in self._hotel_agent.tools), None)
        if self._on_tools_changed is ToolsChanged.PLAN_AGAIN and gone is not None:
            print(f"    orchestrator runtime: {step.id} was planned with {gone.name}, which is gone or changed; plan again")
            step = self.plan("again")

        self._hotel_agent.run(step)


class HotelAgent:
    """Stands in for the Hotel agent: a runtime that reloads its tools when told and runs each call, and a model that
    calls its step's tools in order (for a missing one, the one that takes the same input) and reports it done."""

    def __init__(self, hotels: HotelsServer) -> None:
        self._hotels = hotels
        self.tools = hotels.tools

    def reload_tools(self) -> None:
        self.tools = self._hotels.tools
        print(f"    Hotel agent runtime: reloaded its tools: {', '.join(t.name for t in self.tools)}")

    def run(self, step: Step) -> None:
        result: str | None = step.stay
        for planned in step.tools:
            tool = (next((t for t in self.tools if t.name == planned.name), None)
                    or next((t for t in self.tools if t.takes == planned.takes), None))
            call = ToolCall(tool.name if tool else planned.name, result)   # the model's choice, as data
            swap = "" if call.tool == planned.name else f"has no {planned.name}; "
            print(f"  Hotel agent {swap}asks for {call.tool}({call.argument})")
            if (result := self._hotels.call(call)) is None:
                print(f"  Hotel agent: {step.id} failed")
                return

        print(f"  Hotel agent: {step.id} done")


class HotelsServer:
    """The hotels' MCP server: it lists its tools, sends tools/list_changed when they change, and refuses a call
    to a tool it doesn't list, with MCP's "Unknown tool" error."""

    _SEARCH = Tool("SearchHotels", "city", "hotels")
    _CANCEL = Tool("CancelHotel", "booking", "refund")
    HOLD_THEN_CONFIRM = [_SEARCH, Tool("HoldHotel", "stay", "hold"), Tool("ConfirmHotel", "hold", "booking"), _CANCEL]

    def __init__(self) -> None:
        self.tools = [self._SEARCH, Tool("BookHotel", "stay", "booking"), self._CANCEL]
        self.holds: dict[str, str] = {}
        self.bookings: list[str] = []

    def redeploy(self, tools: list[Tool], notify: HotelAgent) -> None:
        removed = " and ".join(t.name for t in self.tools if t not in tools)
        added = " and ".join(t.name for t in tools if t not in self.tools)
        print(f"  hotels server: removes {removed}, adds {added}; sends tools/list_changed")
        self.tools = tools
        notify.reload_tools()

    def call(self, call: ToolCall) -> str | None:
        tool = next((t for t in self.tools if t.name == call.tool), None)
        stay = self.holds.get(call.argument) if tool and tool.takes == "hold" else call.argument
        if tool is None or stay is None:
            print(f"    hotels server: {f'Unknown tool: {call.tool} (MCP error -32602)' if tool is None else f'no hold {call.argument}'}")
            return None

        if tool.returns == "hold":
            hold = f"H-{len(self.holds) + 1:03}"
            self.holds[hold] = stay
            print(f"    hotels server: {hold} holds a room ({stay}) for 15 minutes")
            return hold

        self.holds.pop(call.argument, None)
        booking = f"B-{len(self.bookings) + 1:03}"
        self.bookings.append(f"{stay} ({booking})")
        print(f"    hotels server: booked {booking} ({stay})")
        return booking


if __name__ == "__main__":
    main()
