# The Fallacies of Distributed Agents

Ten classic fallacies of distributed and enterprise computing, and what each one becomes when a system is
built from many AI agents. Every entry explains the classic fallacy, draws it, maps it to multi-agent
systems, and shows it failing (and fixed) in small, self-contained .NET and Python programs.

> **Work in progress.** Version 1.0 is planned for January 2027.

## The ten

| # | Fallacy | With many agents |
|---|---|---|
| 1 | [The network is reliable](fallacies/01-network-is-reliable/) | Every hop between agents, models, and tools can fail, and a naive retry books the flight twice. |
| 2 | [Latency is zero](fallacies/02-latency-is-zero/) | Every agent turn takes seconds; ask one agent at a time and the user gives up before the plan is ready. |
| 3 | [Bandwidth is infinite](fallacies/03-bandwidth-is-infinite/) | The context window is the bandwidth: forward everything and the runtime silently drops the user's budget. |
| 4 | [The network is secure](fallacies/04-network-is-secure/) | Every call is authenticated, yet a review on a hotel's web page decides who the Payment agent pays. |
| 5 | [Topology doesn't change](fallacies/05-topology-doesnt-change/) | Tools change mid-run, the plan doesn't, and the Hotel agent holds the room instead of booking it. |
| 6 | [There is one administrator](fallacies/06-one-administrator/) | One shared gateway key lets the Research agent refund the hotel payment, and the log names the account owner. |
| 7 | [Transport cost is zero](fallacies/07-transport-cost-is-zero/) | Every call re-sends the whole conversation, so two agents that never agree bill 900,250 tokens for one room. |
| 8 | [The network is homogeneous](fallacies/08-network-is-homogeneous/) | Two providers enforce one schema differently; on the cheaper one, a March 1 trip gets a hotel for January 3. |
| 9 | [Business logic can and should be centralized](fallacies/09-logic-can-be-centralized/) | The approval rule lives only in the orchestrator's prompt; a handoff skips it, and 2,160 EUR is paid unapproved. |
| 10 | [The system is monolithic](fallacies/10-system-is-monolithic/) | A run is not a transaction: the card is declined, the Payment agent says so, and the flight and hotel stay booked. |

Every entry uses the same [example system](docs/example-system.md): a travel assistant with an
orchestrator and four agents.

## This catalog is closed at ten

Corrections, clearer wording, and better examples are welcome through issues; new fallacies are not. The
ten names and their folders will not change.

## Running the examples

Each fallacy has one self-contained file per language, with no dependencies. Each file runs the naive
version and the fixed one and prints what happened. For example:

```text
cd fallacies/01-network-is-reliable
dotnet run dotnet/NetworkIsReliable.cs      # .NET 10
python python/network_is_reliable.py        # Python 3.12+
```

The agents are simulated: no language model, no API keys, no network. The code is written to be read,
not deployed.

## Where the fallacies come from

The first eight are the fallacies of distributed computing, from L. Peter Deutsch and colleagues at Sun
Microsystems. The last two come from Ted Neward's fallacies of enterprise computing.

## How to cite

Díaz Alcolea, C. (2027). *The Fallacies of Distributed Agents* (v1.0).
https://github.com/cdiazal/fallacies-of-distributed-agents

## License

Text and diagrams: [CC BY 4.0](LICENSE). Code: [MIT](LICENSE-CODE).

César Díaz Alcolea · [LinkedIn](https://www.linkedin.com/in/cdiazal/) · [GitHub](https://github.com/cdiazal)
