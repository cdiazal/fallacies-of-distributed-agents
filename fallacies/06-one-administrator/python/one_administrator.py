"""Fallacy 6: There is one administrator.

Run from the entry folder: python python/one_administrator.py   (Python 3.12+, no dependencies)

The Payment agent pays the Garden Inn for trip-42; then the Research agent checks the hotel's cancellation
policy. The Research agent is connected to the payment gateway as well as the web.
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum

PAY_THE_GARDEN_INN = "pay 360 EUR for trip-42 to GARDEN-INN-001"
CHECK_THE_POLICY = "read the Garden Inn's cancellation policy: can CH-001 be refunded?"
GRANTS_BY_THE_GATEWAYS_ADMINISTRATOR = {
    "payment-agent": ["Charge", "Refund"],
}


def main() -> None:
    print("Fallacy 6: There is one administrator")
    print()
    naive()
    print()
    fixed()


def naive() -> None:
    """Naive: the runtime signs every agent's gateway calls with the one key in the team's config."""
    print("Naive: sign every agent's gateway calls with the one key in the team's config")
    gateway = PaymentGateway(GRANTS_BY_THE_GATEWAYS_ADMINISTRATOR)
    host = AgentHost(gateway, credentials=Credentials.SHARED_KEY)                # (1)
    payment_agent = PaymentAgent(tools=PaymentGateway.TOOLS)
    research_agent = ResearchAgent(tools=["FetchPage", *PaymentGateway.TOOLS])   # (2)

    host.run(payment_agent, PAY_THE_GARDEN_INN)
    host.run(research_agent, CHECK_THE_POLICY)                                   # (3)

    print_result(gateway)


def fixed() -> None:
    """Fixed: the runtime asks the gateway for each agent's own key, one tool at a time."""
    print("Fixed: ask the gateway for each agent's own key, one tool at a time")
    gateway = PaymentGateway(GRANTS_BY_THE_GATEWAYS_ADMINISTRATOR)
    host = AgentHost(gateway, credentials=Credentials.KEY_PER_AGENT)   # never one key for all
    payment_agent = PaymentAgent(tools=PaymentGateway.TOOLS)
    research_agent = ResearchAgent(tools=["FetchPage", *PaymentGateway.TOOLS])

    if not host.run(payment_agent, PAY_THE_GARDEN_INN):
        print("  Result: nothing paid for trip-42;"
              " only the gateway's administrator can grant Charge")
        return

    host.run(research_agent, CHECK_THE_POLICY)
    print_result(gateway)


def print_result(gateway: PaymentGateway) -> None:
    print(f"  payment gateway log: {'; '.join(gateway.log)}")
    refund = gateway.refunds.get("CH-001")
    refused = next((entry.split(" ")[-1] for entry in gateway.log if entry.startswith("Refund refused")), None)
    if refund is gateway.secret_key:
        result = f"CH-001 refunded, 360 EUR, and the gateway's log names {refund.holder}, not an agent"
    elif refund is not None:
        result = f"CH-001 refunded, 360 EUR, by {refund.holder}, as the gateway's log shows"
    elif refused:
        result = f"CH-001 stays paid; the gateway refused {refused} and logged it"
    else:
        result = "CH-001 stays paid"
    print(f"  Result: {result}")


# ----- The simulated world ------------------------------------------------------------------


@dataclass(frozen=True)
class ToolCall:
    """A tool call as data: what the model wants done."""
    tool: str
    argument: str


@dataclass(frozen=True)
class Key:
    """A gateway key: who holds it, as the gateway records it, and the tools it can call."""
    id: str
    holder: str
    tools: tuple[str, ...]


class Credentials(Enum):
    SHARED_KEY = "shared key"
    KEY_PER_AGENT = "key per agent"


class Agent:
    """An agent as its runtime sees it: an identity, a display name, the tools it's connected to, and a model."""

    def __init__(self, agent_id: str, name: str, tools: list[str]) -> None:
        self.id = agent_id
        self.name = name
        self.tools = tools

    def decide(self, task: str) -> list[ToolCall]:
        raise NotImplementedError


class PaymentAgent(Agent):
    """Stands in for the Payment agent's model: it asks to charge the amount and the account its task names."""

    def __init__(self, tools: list[str]) -> None:
        super().__init__("payment-agent", "Payment agent", tools)

    def decide(self, task: str) -> list[ToolCall]:
        return [ToolCall("Charge", f"{task.split(' ')[1]} EUR, {task.split(' ')[-1]}")]


class ResearchAgent(Agent):
    """Stands in for the Research agent's model: it reads the page its task names, then calls every other tool on
    its list whose name the task contains, on the charge the task names."""

    def __init__(self, tools: list[str]) -> None:
        super().__init__("research-agent", "Research agent", tools)

    def decide(self, task: str) -> list[ToolCall]:
        return [ToolCall("FetchPage", "Garden Inn cancellation policy"),
                *(ToolCall(tool, task.split(" ")[-3]) for tool in self.tools
                  if tool != "FetchPage" and tool.lower() in task.lower())]


class AgentHost:
    """Stands in for the agents' runtime, which runs each tool call a model asks for. With SHARED_KEY, it signs
    gateway calls with the key in the team's config; with KEY_PER_AGENT, it asks the gateway for a key for the
    agent it's running and this one tool, and calls nothing if the gateway refuses. The model never sees a key."""

    def __init__(self, gateway: PaymentGateway, credentials: Credentials) -> None:
        self._gateway = gateway
        self._credentials = credentials

    def run(self, agent: Agent, task: str) -> bool:
        for call in agent.decide(task):
            print(f"  {agent.name} asks for {call.tool}({call.argument})")
            if call.tool == "FetchPage":
                print("    web: \"Free cancellation up to 48 hours before arrival.\"")
                continue

            key = self._gateway.secret_key if self._credentials is Credentials.SHARED_KEY else None
            print(f"    runtime: asks the payment gateway for a {agent.id} key for {call.tool}"
                  if key is None else f"    runtime: signs with {key.id}, {key.holder}'s key for every tool")
            key = key or self._gateway.issue(agent.id, call.tool)
            if key is None or not self._gateway.call(key, call):
                return False

        return True


class PaymentGateway:
    """The payment gateway behind Charge and Refund. Its administrator, not the travel team, grants each agent its
    tools. The account's secret key came with the account: every tool, no expiry, and no grant applies to it."""

    TOOLS = ["Charge", "Refund"]

    def __init__(self, grants: dict[str, list[str]]) -> None:
        self._grants = grants
        self.secret_key = Key("KEY-001", "the account owner", tuple(self.TOOLS))
        self.log: list[str] = []
        self.refunds: dict[str, Key] = {}
        self._charges: dict[str, str] = {}
        self._keys = 1

    def issue(self, agent: str, tool: str) -> Key | None:
        if tool not in self._grants.get(agent, []):
            print(f"    payment gateway: refused; its administrator grants {agent} no {tool}")
            self.log.append(f"{tool} refused to {agent}")
            return None

        self._keys += 1
        key = Key(f"KEY-{self._keys:03}", agent, (tool,))
        print(f"    payment gateway: issued {key.id} to {agent}, for {tool} only")
        return key

    def call(self, key: Key, call: ToolCall) -> bool:
        charge = f"CH-{len(self._charges) + 1:03}" if call.tool == "Charge" else call.argument
        refundable = charge in self._charges and charge not in self.refunds
        if call.tool not in key.tools or (call.tool == "Refund" and not refundable):
            print(f"    payment gateway: refused {call.tool}({call.argument})")
            return False

        if call.tool == "Charge":
            self._charges[charge] = call.argument.split(", ")[0]
            print(f"    payment gateway: paid {self._charges[charge]} to {call.argument.split(', ')[1]} ({charge})")
            self.log.append(f"{charge} charged by {key.holder}")
            return True

        self.refunds[charge] = key
        print(f"    payment gateway: refunded {self._charges[charge]} on {charge}")
        self.log.append(f"{charge} refunded by {key.holder}")
        return True


if __name__ == "__main__":
    main()
