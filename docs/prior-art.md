# Prior art

Others got here first. Several authors have borrowed the fallacies of distributed computing to talk about
AI, and people from distributed systems have been showing that agents fail the way distributed systems
do. This page lists them and how this catalog differs. It differs in shape: a closed list of ten, the
eight fallacies of distributed computing and two of Ted Neward's fallacies of enterprise computing, each
mapped one to one onto systems built from many agents, with a diagram and a runnable .NET program and
Python program per entry.

## Lists of fallacies

- [AI Agents and fallacies of Distributed Computing](https://blog.gopenai.com/ai-agents-and-fallacies-of-distributed-computing-e45fadd1c765)
  (Krishna Avva, July 26, 2026). The closest to this catalog: it walks through the eight fallacies one by
  one as they show up in agents, with the fixes that hold, and argues that invariants belong in the
  orchestrator, enforced deterministically, never in the model. It covers *"one agent calling
  deterministic tools"*; this catalog maps the same eight to a system of many agents, adds two of Ted
  Neward's fallacies of enterprise computing, and shows each one failing and fixed in a runnable .NET
  program and Python program.
- [The Fallacies of GenAI Development](https://dev.to/bala_paranj_059d338e44e7e/the-fallacies-of-genai-development-1m54)
  (Bala Paranj, May 28, 2026). A nine-part series that opens with Deutsch's list and names eight false
  beliefs about AI-assisted development, such as the belief that more agents mean more productivity. It's
  about building software with AI; this catalog is about systems made of agents.
- [The Fallacies of AI-Assisted Programming](https://writing.alteredcraft.com/p/the-fallacies-of-ai-assisted-programming)
  (Sam Keen, June 3, 2026). Eight assumptions that held while a human wrote every line and quietly fail
  with AI authors, such as deterministic output and an endless context window. Keen writes that the eight
  are *"not a one-to-one re-map of Deutsch's list"*; this catalog's first eight entries are that re-map,
  for multi-agent systems, and six of its entries credit Keen.
- [Agentic Development Fallacies](https://thuva4.com/blog/agentic-development-fallacies/) (Thuvarakan
  Tharmarajasingam, August 15, 2026). Eight one-line fallacies of developing software with agents, framed
  after the fallacies of distributed computing, with one idea under them: when implementation gets cheap,
  everything it used to pay for, from specification to verification, gets relatively more expensive. This
  catalog is about running software made of agents.
- [Fallacies for AI Agent Development](https://agentpatterns.ai/fallacies/) (AgentPatterns.ai). An index
  of wrong mental models about AI tools, on a reference site for engineers who build with coding
  assistants. It doesn't start from Deutsch; every entry here starts from a classic fallacy.

## Essays and guidance

- [Things I Think I Think... Preferring Local OSS LLMs](https://blogs.newardassociates.com/blog/2026/titit-local-ai.html)
  (Ted Neward, April 1, 2026). Lists the eight fallacies and argues that AI coding tools built on cloud
  models depend on many network links and providers, so they inherit the fallacies; it works through the
  first and argues for running models locally. This catalog maps all ten to systems of many agents.
- [Multi-Agent Systems Have a Distributed Systems Problem](https://christophermeiklejohn.com/ai/agents/distributed/zabriskie/2026/03/30/multi-agent-systems-have-a-distributed-systems-problem.html)
  (Christopher Meiklejohn, March 30, 2026). Argues that agents sharing state hit five categories of
  failure, from stale reads to Byzantine faults, and points to vector clocks and CRDTs; a later series by
  the same author,
  [Getting Up to Speed on Multi-Agent Systems](https://christophermeiklejohn.com/ai/agents/mas-series/2026/04/24/mas-series-01-the-landscape.html),
  surveys the research. This catalog is organized around false beliefs, each one in code.
- [Your Agent is a Distributed System (and fails like one)](https://maheshba.bitbucket.io/blog/2026/04/24/agentfailures.html)
  (Mahesh Balakrishnan, April 24, 2026). Names eight failure modes, such as the Crashed Agent and the
  Rogue Agent, and presents LogAct, work with colleagues that puts an agent's actions on a shared log, as
  a first step. It starts from failure modes; this catalog starts from false beliefs, each with a fix you
  can run.
- [Agentic Systems Are Distributed Systems](https://tianpan.co/blog/2026/05/05/agentic-systems-distributed-services-microservices-patterns)
  (Tian Pan, May 5, 2026). Maps microservices patterns onto agents: circuit breakers, bulkheads, event
  sourcing, and idempotency keys. An earlier post by the same author,
  [The CAP Theorem for AI Agents](https://tianpan.co/blog/2026/04/14/cap-theorem-for-ai-agents), cites
  three of the fallacies directly. This catalog arranges similar lessons as a closed list.
- [Your Multi-Agent System Is a Distributed System. Treat It Like One](https://hackernoon.com/your-multi-agent-system-is-a-distributed-system-treat-it-like-one)
  (Veera Ravindra Divi, July 6, 2026). The closest in spirit: it reads agent failures through a few of
  the fallacies, shows a short Python idempotency wrapper, and tells readers to ask *"which fallacy of
  distributed computing you just rediscovered."* This catalog reads agent failures through all eight, and
  through two of Ted Neward's fallacies of enterprise computing.
- [Agentic AI Lens](https://docs.aws.amazon.com/wellarchitected/latest/agentic-ai-lens/agentic-ai-lens.html)
  (AWS Well-Architected, June 10, 2026). Guidance rather than an essay: best practices for agents on AWS,
  noting that multi-agent systems bring *"distributed failure modes."* This catalog is vendor-neutral.

## Research

- [Language Model Teams as Distributed Systems](https://arxiv.org/abs/2603.12229) (Elizabeth Mieczkowski
  et al., March 12, 2026). Proposes distributed systems as a principled basis for LLM teams and tests it
  on coding tasks: Amdahl's law bounds a team's speedup, stragglers slow teams with fixed assignments,
  and agents that edit the same files overwrite each other's work. It measures the analogy in
  experiments; this catalog explains ten false beliefs and shows each one failing in code.
- [SagaLLM](https://arxiv.org/abs/2503.11951) (Edward Y. Chang and Longling Geng, March 15, 2025). Brings
  the Saga pattern, compensation, and validation agents to multi-agent planning. It uses the model to
  help orchestrate recovery; [fallacy 10](../fallacies/10-system-is-monolithic/) reaches the same pattern
  from the original 1987 paper and keeps every model out of the undo.
- [Robust Agent Compensation (RAC)](https://arxiv.org/abs/2605.03409) (Srinath Perera, Kaviru
  Hapuarachchi, Frank Leymann, and Rania Khalaf, May 5, 2026). A log-based recovery layer that agent
  frameworks can add without changing agent code, undoing completed tool calls in reverse order.
  [Fallacy 10](../fallacies/10-system-is-monolithic/) credits it: its fix is the same idea at the size of
  an example, with the runtime, not a model, undoing each booking newest first.
- [CoAgent: Concurrency Control for Multi-Agent Systems](https://arxiv.org/abs/2606.15376) (Hongtao Lyu
  et al., June 13, 2026). Tool-call middleware for agents that change shared state, such as a git tree or
  a Kubernetes cluster. Locks block through minutes of inference and optimistic concurrency throws that
  work away, so the runtime tells an agent when a write affects its plan, and the agent repairs it. Like
  [fallacy 10](../fallacies/10-system-is-monolithic/), it undoes writes through an inverse each tool
  registers in advance. It goes deep on one problem; this catalog maps ten.

[← All ten fallacies](../README.md)
