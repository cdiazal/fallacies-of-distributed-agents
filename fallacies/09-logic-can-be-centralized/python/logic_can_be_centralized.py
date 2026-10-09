"""Fallacy 9: Business logic can and should be centralized.

Run from the entry folder: python python/logic_can_be_centralized.py   (Python 3.12+, no dependencies)

The example system with one change: the agents run as a handoff workflow, so the Hotel agent can hand
off to the Payment agent. trip-42 books 6 rooms; payments over 2,000 EUR need the travel manager's approval.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from enum import Enum
from typing import Protocol

BOOK_THE_ROOMS = "Book 6 rooms at the Garden Inn for 2 nights from March 1."


def main() -> None:
    print("Fallacy 9: Business logic can and should be centralized")
    print()
    naive()
    print()
    fixed()


def naive() -> None:
    """Naive: the runtime writes the rule only into the orchestrator's instructions, and Charge pays any amount."""
    print("Naive: write the approval rule in the orchestrator's instructions, and let the agents hand off")
    hotels, gateway = Hotels(), PaymentGateway()
    rule = ApprovalRule(over_eur=2_000, approver="the travel manager")
    runtime = AgentRuntime(hotels, gateway, rule, rule_in=Place.INSTRUCTIONS)   # (1)
    hotel_agent = HotelAgent(handoffs=["orchestrator", "Payment agent"])        # (2)
    payment_agent = PaymentAgent()                                              # (3)

    runtime.run(BOOK_THE_ROOMS, Orchestrator(), hotel_agent, payment_agent)
    print_result(runtime, gateway, rule)


def fixed() -> None:
    """Fixed: Charge runs the rule for every caller, and the instructions are written from the same rule."""
    print("Fixed: run the approval rule in Charge, and write the instructions from it")
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


def print_result(runtime: AgentRuntime, gateway: PaymentGateway, rule: ApprovalRule) -> None:
    if runtime.held is not None:
        result = f"B-001 booked; {runtime.held:,} EUR waits for {rule.approver}'s approval (AR-001), nothing paid yet"
    elif (paid := gateway.paid) is not None:
        result = f"{paid:,} EUR paid for B-001 (CH-001), " + ("and no one was asked to approve it"
            if paid > rule.over_eur else f"not over {rule.over_eur:,} EUR, so no approval needed")
    else:
        result = "B-001 booked, nothing paid; no approval request was filed"
    print(f"  Result: {result}")


# ----- The simulated world ------------------------------------------------------------------

@dataclass(frozen=True)
class Turn:
    """What a model wants next, as data: a tool call, a handoff, or a reply. The runtime carries it out."""
    tool: str | None = None
    argument: str | None = None
    hand_off_to: str | None = None
    say: str | None = None


# Where the runtime puts the approval rule: in the orchestrator's instructions, or in Charge.
Place = Enum("Place", ["INSTRUCTIONS", "CHARGE"])


@dataclass(frozen=True)
class ApprovalRule:
    """The travel policy's rule, defined once; the orchestrator's instructions are written from it either way."""
    over_eur: int
    approver: str | None

    @property
    def as_rule(self) -> str:
        return f"Payments over {self.over_eur:,} EUR need {self.approver or 'someone'}'s approval."

    @property
    def as_fact(self) -> str:
        if self.approver is None:
            return f"Charge refuses payments over {self.over_eur:,} EUR."
        return f"Charge holds payments over {self.over_eur:,} EUR for {self.approver}'s approval."

    @staticmethod
    def eur(text: str) -> int | None:
        """How the models and Charge read an amount in text: "2,160 EUR" is 2160."""
        return int(m.group(1).replace(",", "")) if (m := re.search(r"([\d,]+) EUR", text)) else None


class Agent(Protocol):
    """An agent in the workflow: the runtime calls its model with its instructions and the conversation so far."""
    name: str

    def take(self, instructions: str, conversation: list[str]) -> Turn: ...


@dataclass(frozen=True)
class Orchestrator:
    """The orchestrator's model (no tools): it hands each task on, but stops a payment its instructions say needs approval, unless they leave it to Charge."""
    name: str = "orchestrator"

    def take(self, instructions: str, conversation: list[str]) -> Turn:
        if (amount := ApprovalRule.eur(conversation[-1])) is None:
            return Turn(hand_off_to="Hotel agent", say=conversation[-1])
        if not instructions.startswith("Charge") and amount > ApprovalRule.eur(instructions):
            return Turn(say=f"B-001 costs {amount:,} EUR. {instructions}")
        return Turn(hand_off_to="Payment agent", say=conversation[-1])


@dataclass(frozen=True)
class HotelAgent:
    """The Hotel agent's model: it books the rooms its task names, then hands the payment to the Payment agent if it may, or back to the orchestrator."""
    handoffs: list[str]
    name: str = "Hotel agent"

    def take(self, instructions: str, conversation: list[str]) -> Turn:
        if (last := conversation[-1]).startswith("booked"):
            return Turn(hand_off_to="Payment agent" if "Payment agent" in self.handoffs else "orchestrator",
                        say=f"B-001 is booked. Pay {ApprovalRule.eur(last):,} EUR to {last.split(' ')[-1]}.")
        return Turn(tool="BookHotel", argument=f"Garden Inn, 2027-03-01, 2 nights, {' '.join(last.split(' ')[1:3])}")


@dataclass(frozen=True)
class PaymentAgent:
    """The Payment agent's model: it pays what its last message names, and tells the traveler; no approval rule in its instructions."""
    name: str = "Payment agent"

    def take(self, instructions: str, conversation: list[str]) -> Turn:
        if (last := conversation[-1]).startswith("B-001 is"):
            return Turn(tool="Charge", argument=f"{ApprovalRule.eur(last):,} EUR, {last.split(' ')[-1].rstrip('.')}")
        return Turn(say=f"B-001: {last}.")


class AgentRuntime:
    """The agents' runtime, as in a handoff workflow: it runs each turn's tool calls and passes control on each handoff."""

    def __init__(self, hotels: Hotels, gateway: PaymentGateway, rule: ApprovalRule, rule_in: Place) -> None:
        self._hotels, self._gateway, self._rule, self._rule_in = hotels, gateway, rule, rule_in
        self.held: int | None = None

    def run(self, request: str, orchestrator: Agent, *agents: Agent) -> bool:
        """Runs the request from the orchestrator; False when Charge refused a payment no one is set to approve."""
        instructions = self._rule.as_rule if self._rule_in is Place.INSTRUCTIONS else self._rule.as_fact
        print(f"  runtime: instructs the orchestrator: \"{instructions}\"")
        current, conversation = orchestrator, [request]
        while (turn := current.take(instructions if current is orchestrator else "", conversation)).tool or turn.hand_off_to:
            if (tool := turn.tool) is not None:
                print(f"  {current.name} asks for {tool}({turn.argument})")
                if (result := self._hotels.book(turn.argument) if tool == "BookHotel" else self._charge(turn.argument)) is None:
                    return False
                conversation.append(result)
                continue

            print(f"  {current.name} hands off to the {turn.hand_off_to}: \"{turn.say}\"")
            if current is not orchestrator and turn.hand_off_to != orchestrator.name:
                print(f"    runtime: hands control to the {turn.hand_off_to}, past the orchestrator and its instructions, no error")
            current = orchestrator if turn.hand_off_to == orchestrator.name else next(a for a in agents if a.name == turn.hand_off_to)
            conversation.append(turn.say)

        print(f"  {current.name} replies to the traveler: \"{turn.say}\"")
        return True

    def _charge(self, argument: str) -> str | None:
        """Charge as the runtime runs it. With the rule here, a payment over the limit waits for its approver."""
        amount, rule = ApprovalRule.eur(argument), self._rule
        if self._rule_in is Place.INSTRUCTIONS or amount <= rule.over_eur:
            return self._gateway.charge(amount, argument.split(", ")[-1])
        if rule.approver is None:   # no one can approve it: refuse, and never fall back to paying
            print(f"    runtime: Charge refuses {amount:,} EUR, over {rule.over_eur:,} EUR: no one is set to approve it")
            return None

        self.held = amount
        print(f"    runtime: Charge holds {amount:,} EUR, over {rule.over_eur:,} EUR, for {rule.approver}'s approval (AR-001)")
        return f"held for {rule.approver}'s approval (AR-001)"   # a result the model reports, not an error it retries


class Hotels:
    """The hotels behind BookHotel: 180 EUR a room a night at the Garden Inn, paid to its account on file."""

    def book(self, argument: str) -> str:
        rooms = argument.split(", ")[3]
        total = 180 * 2 * int(rooms.split(" ")[0])
        print(f"    hotels: booked B-001 (Garden Inn, {rooms}, March 1-3, {total:,} EUR)")
        return f"booked B-001 for {total:,} EUR, payable to GARDEN-INN-001"


class PaymentGateway:
    """The payment gateway behind Charge: it pays whatever it's asked to."""

    def __init__(self) -> None:
        self.paid: int | None = None

    def charge(self, amount: int, payee_account: str) -> str:
        self.paid = amount
        print(f"    payment gateway: paid {amount:,} EUR to {payee_account} (CH-001)")
        return f"paid {amount:,} EUR to {payee_account} (CH-001)"


if __name__ == "__main__":
    main()
