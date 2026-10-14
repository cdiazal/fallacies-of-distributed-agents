# 5. Topology doesn't change

> *"Topology doesn't change."* — the fifth of the eight fallacies of distributed computing, from L. Peter
> Deutsch and colleagues at Sun Microsystems.

## The fallacy

Servers are decommissioned, replaced, and moved to other subnets; clients connect and disconnect; and in
the cloud, machines and containers come and go as the load changes. As David Boike put it, "A network
diagram on paper is a lie waiting to happen."

Believing topology doesn't change means hardcoded addresses, and moving them to a file doesn't help: in
Boike's words, "An IP address in a config file is still hardcoded." It also means routes worked out in
advance. Arnon Rotem-Gal-Oz points to WS-Routing, which assumed that "a message can know the path it
needs to travel in advance," and calls next-hop routing, the way TCP/IP works, "more robust."

The classic answers keep names apart from places. Use a name, not an address, and resolve it when you
call, through service discovery (ZooKeeper, etcd, Consul). Cache a resolution only for its time to live,
as DNS does, and resolve again when a call fails. Boike adds that DNS TTLs are too slow for real-time
changes; that's what discovery is for. And test by turning things off.

Lookups are exact: a name that's gone fails with an error, and a stale address is usually refused or times
out. Nothing answers with the nearest match.

## The picture

![Left: a client that hardcodes orders at 10.0.0.7 times out on the decommissioned server; resolving
orders through a registry, it calls the new server, 10.0.0.9, directly; a stale name or address fails with
an error, never with the nearest match, and the fix is to resolve names when you call, cache them only for
their TTL, and resolve again on failure. Right: at turn 1, the orchestrator plans step-2 with BookHotel;
the Hotels server then replaces BookHotel with HoldHotel and ConfirmHotel and sends tools/list_changed, and
the Hotel agent reloads its tools; its model calls the closest tool, HoldHotel, and reports step-2 done, so
no room is booked and hold H-001 lapses; the fix is to check each step's tools against the agent's current
ones before it runs and, if they changed, plan again.](diagram.excalidraw.svg)

## With many agents

In a multi-agent system, the topology is every agent and tool a run can reach, and what each one does. It
can change in the middle of a run: an MCP server's set of tools "MAY change over time"
([MCP](https://modelcontextprotocol.io/specification/2026-07-28/server/tools), 2026-07-28), and a run lasts
minutes, or "days or even weeks" when it waits for a person
([Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/hosting/azure-functions)).
MCP already carries the classic answer: a server that declares `listChanged` should send
`notifications/tools/list_changed` to clients that listen for it, and a tool list carries a time to live,
"analogous to HTTP Cache-Control: max-age"
([MCP caching](https://modelcontextprotocol.io/specification/2026-07-28/server/utilities/caching)).

Two things are new. First, the plan is a copy of the topology that no notification reaches. At turn 1 the
orchestrator's model writes it in words, from the tool list it saw then, and it stays in the
orchestrator's context. The notification refreshes the Hotel agent's list, not a sentence a model wrote.
Second, a stale name doesn't have to fail: a model can match it to a tool that's still there. An MCP server
looks a name up exactly and answers an unknown one with an error. A model picks tools by what they say
they do: models "rely entirely on the text descriptions of tools to decide which ones to use"
([Faghih et al., 2025](https://arxiv.org/abs/2505.18135)), and in 2024,
[ToolBeHonest](https://aclanthology.org/2024.emnlp-main.637/) traced their errors mainly to judging
whether a task can be done with the tools at hand. So a step that names a missing tool can become a
successful call to the closest one. Here, the classic answer backfires: reloading the list is what turns a
loud failure into a silent one.

## In code

Both programs simulate the [example system](../../docs/example-system.md): at turn 1, from the Hotel
agent's list of tools, the orchestrator plans step-2 of trip-42, two nights at the Garden Inn. Before the
step runs, the hotels' MCP server is redeployed: HoldHotel (which holds a room for 15 minutes) and
ConfirmHotel replace BookHotel. In both halves, the server sends `notifications/tools/list_changed` and the
Hotel agent's runtime reloads its tools. The orchestrator's model plans a step as a chain of the agent's
tools, in list order, from the stay toward a booking; the Hotel agent's model calls its step's tools in
order, swapping any it no longer has for one that takes the same input. Each program has no dependencies
and runs a naive version, then a fixed one. From this folder:

```text
dotnet run dotnet/TopologyDoesntChange.cs      # .NET 10
python python/topology_doesnt_change.py        # Python 3.12+
```

The naive orchestrator runs the plan from turn 1 as written
([full .NET file](dotnet/TopologyDoesntChange.cs), [full Python file](python/topology_doesnt_change.py)):

```csharp
var orchestrator = new Orchestrator(hotelAgent, onToolsChanged: ToolsChanged.KeepPlan);   // (1)
var step = orchestrator.Plan("at turn 1");                                                // (2)
hotels.Redeploy(HotelsServer.HoldThenConfirm, notify: hotelAgent);
orchestrator.Run(step);                                                                   // (3)
```

```python
orchestrator = Orchestrator(hotel_agent, on_tools_changed=ToolsChanged.KEEP_PLAN)   # (1)
step = orchestrator.plan("at turn 1")                                               # (2)
hotels.redeploy(HotelsServer.HOLD_THEN_CONFIRM, notify=hotel_agent)
orchestrator.run(step)                                                              # (3)
```

Both programs print the same output. The naive half:

```text
Naive: run the plan from turn 1 as written
  orchestrator plans at turn 1, with the Hotel agent's tools: SearchHotels, BookHotel, CancelHotel
    step-2: Hotel agent, BookHotel(Garden Inn, 2 nights)
  hotels server: removes BookHotel, adds HoldHotel and ConfirmHotel; sends tools/list_changed
    Hotel agent runtime: reloaded its tools: SearchHotels, HoldHotel, ConfirmHotel, CancelHotel
  Hotel agent has no BookHotel; asks for HoldHotel(Garden Inn, 2 nights)
    hotels server: H-001 holds a room (Garden Inn, 2 nights) for 15 minutes
  Hotel agent: step-2 done
  Result: no room booked for trip-42; hold H-001 lapses in 15 minutes
```

## Where it breaks

**(1) The orchestrator's runtime runs the plan as written.** The Hotel agent reloaded its tools when the
server said so, but nothing checks the plan against them.

**(2) The plan names the tools of turn 1.** The orchestrator's model saw them once and wrote them down,
and text has no time to live.

**(3) The Hotel agent's model calls the closest tool it has.** HoldHotel takes the same stay, and the step
names a call, not the booking it's for, so the agent reports step-2 done. Without the reload, BookHotel
would have failed with "Unknown tool."

The fixed version still plans step-2 at turn 1, and the Hotel agent's model still swaps a missing tool for
the closest one; what changes is that the orchestrator's runtime checks each step's tools against the
agent's current ones before handing the step over, and asks for a new plan when they differ.

### How to avoid it

Re-resolve the plan before every step. Each step keeps the full tool definitions it was planned with (or
a hash of them, like an ETag), and the runtime compares them with the agent's current list, fetched again
if its TTL has run out, before it hands the step over. If any is gone or has changed, the orchestrator's
model plans that step again with the current list. The runtime owns the check; the model never decides
whether its plan is still valid.
[Fallacy 1](../01-network-is-reliable/) retried the effect, not the reasoning; when the topology changes,
the reasoning is what has to run again. This is
[client-side service discovery](https://microservices.io/patterns/client-side-discovery.html) applied to a
plan: the client finds the service "when making a request," not when the plan was written.

```csharp
var orchestrator = new Orchestrator(hotelAgent, onToolsChanged: ToolsChanged.PlanAgain);
try
{
    var step = orchestrator.Plan("at turn 1");   // the step keeps full tool definitions
    hotels.Redeploy(HotelsServer.HoldThenConfirm, notify: hotelAgent);
    orchestrator.Run(step);
    PrintResult(hotels);
}
catch (NoPlanException)
{
    Console.WriteLine("  Result: no room booked yet; tell the traveler");
}
```

```python
orchestrator = Orchestrator(hotel_agent, on_tools_changed=ToolsChanged.PLAN_AGAIN)
try:
    step = orchestrator.plan("at turn 1")   # the step keeps full tool definitions
    hotels.redeploy(HotelsServer.HOLD_THEN_CONFIRM, notify=hotel_agent)
    orchestrator.run(step)
    print_result(hotels)
except NoPlanError:
    print("  Result: no room booked yet; tell the traveler")
```

And the fixed half of the output:

```text
Fixed: before each step, check its tools against the agent's current ones
  orchestrator plans at turn 1, with the Hotel agent's tools: SearchHotels, BookHotel, CancelHotel
    step-2: Hotel agent, BookHotel(Garden Inn, 2 nights)
  hotels server: removes BookHotel, adds HoldHotel and ConfirmHotel; sends tools/list_changed
    Hotel agent runtime: reloaded its tools: SearchHotels, HoldHotel, ConfirmHotel, CancelHotel
    orchestrator runtime: step-2 was planned with BookHotel, which is gone or changed; plan again
  orchestrator plans again, with the Hotel agent's tools: SearchHotels, HoldHotel, ConfirmHotel, CancelHotel
    step-2: Hotel agent, HoldHotel(Garden Inn, 2 nights), then ConfirmHotel(hold)
  Hotel agent asks for HoldHotel(Garden Inn, 2 nights)
    hotels server: H-001 holds a room (Garden Inn, 2 nights) for 15 minutes
  Hotel agent asks for ConfirmHotel(H-001)
    hotels server: booked B-001 (Garden Inn, 2 nights)
  Hotel agent: step-2 done
  Result: 1 room booked for trip-42: Garden Inn, 2 nights (B-001)
```

If the current tools can't complete a booking (say, the server is mid-deploy and lists HoldHotel without
ConfirmHotel), the runtime calls nothing, and the traveler is told. The check narrows the window but
doesn't close it: if the tools change after the check, the Hotel agent reloads them and its model swaps
again. So have the Hotel agent's runtime refuse any call outside the step's definitions, and if you own the
server, have it refuse calls made against an older definition, the way
[`If-Match`](https://www.rfc-editor.org/rfc/rfc9110#name-if-match) refuses a write against an old ETag.
Undoing steps that already finished is [fallacy 10](../10-system-is-monolithic/).

In real code, the check is a few lines in either language. The libraries refresh lists, not plans:
Microsoft Agent Framework's Python MCP tools
[reload theirs](https://github.com/microsoft/agent-framework/blob/main/python/packages/core/agent_framework/_mcp.py),
and the MCP C# SDK [shows how](https://csharp.sdk.modelcontextprotocol.io/v1/concepts/tools/tools.html).
Check one default too: in Microsoft.Extensions.AI,
[`TerminateOnUnknownCalls`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.functioninvokingchatclient.terminateonunknowncalls)
is `false`, so when a model asks for a tool the client doesn't have, the client tells it the tool
"couldn't be found" and lets it choose again, which invites the same swap one model call later. Set it to
`true`, and let your code, not the model, decide to plan again.

The example leaves out three things to stay short. The world changes too: another run can take the last
room between search and booking, so make the booking itself conditional, with `If-Match`, instead of
holding a lock across turns. If a newer run replaces a paused one, fence the old one off with a token
([Martin Kleppmann](https://martin.kleppmann.com/2016/02/08/how-to-do-distributed-locking.html)). And
whether a changed definition is still compatible is [fallacy 8](../08-network-is-homogeneous/).

---

**Read more**

- [The Eight Fallacies of Distributed Computing](https://nighthacks.com/jag/res/Fallacies.html) (L. Peter
  Deutsch, on James Gosling's site)
- [Fallacy #5: Topology doesn't change](https://particular.net/blog/topology-doesnt-change) (David Boike,
  Particular Software, 2019)
- [Fallacies of Distributed Computing Explained](https://web.archive.org/web/20070204070434/http://www.rgoarchitects.com:80/Files/fallacies.pdf)
  (Arnon Rotem-Gal-Oz, archived PDF)
- [Model Context Protocol: Tools](https://modelcontextprotocol.io/specification/2026-07-28/server/tools)
  (specification, 2026-07-28 revision)
- [Tool Preferences in Agentic LLMs are Unreliable](https://arxiv.org/abs/2505.18135) (Kazem Faghih et al.,
  EMNLP 2025)

[← All ten fallacies](../../README.md)
