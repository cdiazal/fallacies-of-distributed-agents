# 6. There is one administrator

> *"There is one administrator."* — the sixth of the eight fallacies of distributed computing, from L.
> Peter Deutsch and colleagues at Sun Microsystems.

## The fallacy

No one runs the whole system. Databases, networks, servers, and every partner's and provider's services
have administrators of their own, and some of them, as Arnon Rotem-Gal-Oz put it, "are not even under your
control and they may have their own agendas/rules." David Boike was blunter: "There is never one
administrator."

Believing there is one administrator means one credential, set up once, in every component's config: the
same database login for the billing service, the reports job, and the admin tool. The logs then name the
credential, not the component, and the database's administrator can only revoke it for everyone at once.
Rotem-Gal-Oz also warns that administrators can constrain you with "limited privileges, limited ports and
protocols."

The classic answer is for each resource's owner to issue each component its own credential, with only the
rights it needs, for a limited time. Jerome Saltzer and Michael Schroeder called it
[least privilege](https://www.cs.virginia.edu/~evans/cs551/saltzer/) in 1975: "the least set of privileges
necessary to complete the job," which "limits the damage that can result from an accident or error."
Michael Nygard applies it to the host: "each major application should have its own user"
([*Release It!*](https://media.pragprog.com/titles/mnee2/security.pdf)), and OAuth tokens stand for
"specific scopes and durations of access, granted by the resource owner"
([RFC 6749](https://www.rfc-editor.org/rfc/rfc6749#section-1.4)).

A service makes only the calls its code makes, so an over-broad key mostly sits unused.

## The picture

![Left: three services share one database login, so the audit log names the login, not the service, and
the database's administrator can only revoke it for everyone; the fix is for each owner to issue each
component its own scoped, short-lived credential. Right: one runtime signs every agent's gateway calls with
KEY-001, the account owner's key, which no grant limits; the Research agent, asked whether CH-001 can be
refunded, calls Refund(CH-001), and the payment gateway's log says the account owner charged and refunded
CH-001; the fix is one identity per agent and a key per call, issued under the gateway administrator's
grants.](diagram.excalidraw.svg)

## With many agents

In a multi-agent system, everything an agent touches has an administrator of its own: the model provider,
each tool server's owner, the payment provider, the cloud, and your team. The protocols and providers
already carry the classic answer. MCP servers "MUST validate that access tokens were issued specifically
for them" ([MCP](https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization),
2026-07-28), and Stripe tells you to create restricted keys and "assign them to different parts of your
application" ([Stripe](https://docs.stripe.com/keys)).

Two things are new. First, the key, not the code, decides what an agent does. A classic service can make
only the calls written in its code, so a right its code never uses sits idle until the key leaks. An
agent's model picks its calls at run time from whatever it can reach, so an honest model, on an ordinary
question, can use the extra right, with no thief and no injection: OWASP counts it as excessive agency
"regardless of what is causing the LLM to malfunction"
([OWASP LLM06](https://genai.owasp.org/llmrisk/llm062025-excessive-agency/)). For secret keys, Stripe
warns: "An agent might do things you don't want in your account"
([Stripe](https://docs.stripe.com/keys/restricted-api-keys)). Second, many agents share one identity by
default. A classic identity belongs to the deployable unit: one application, one user. Agents live several
to a process, on one client and one credential, so even the classic answer, a restricted key for the
travel app, hands the Payment agent's Refund to the Research agent. The gateway sees one caller, and its
administrator can only revoke them all at once. That's why platforms now issue agent identities: in
Microsoft Entra Agent ID, "The subject of the access token is the agent identity"
([Microsoft](https://learn.microsoft.com/en-us/entra/agent-id/agent-identities)).

## In code

Both programs simulate the [example system](../../docs/example-system.md) with one change: the Research
agent is connected to the payment gateway as well as the web, and sees every tool the gateway lists. The
Payment agent pays the Garden Inn for trip-42 (CH-001, 360 EUR); then the orchestrator asks the Research
agent whether CH-001 can be refunded under the hotel's cancellation policy. The gateway's administrator has
granted payment-agent Charge and Refund, and research-agent nothing. The Payment agent's model charges what
its task names; the Research agent's model reads the page, then calls every other tool on its list that the
task names: asked "can CH-001 be refunded?", it takes the question as a request and refunds it. Each
program has no dependencies and runs a naive version, then a fixed one. From this folder:

```text
dotnet run dotnet/OneAdministrator.cs      # .NET 10
python python/one_administrator.py        # Python 3.12+
```

The naive runtime signs every agent's gateway calls with the one key in the team's config
([full .NET file](dotnet/OneAdministrator.cs), [full Python file](python/one_administrator.py)):

```csharp
var gateway = new PaymentGateway(grantsByTheGatewaysAdministrator);
var host = new AgentHost(gateway, credentials: Credentials.SharedKey);                  // (1)
var paymentAgent = new PaymentAgent(tools: PaymentGateway.Tools);
var researchAgent = new ResearchAgent(tools: ["FetchPage", .. PaymentGateway.Tools]);   // (2)

host.Run(paymentAgent, PayTheGardenInn);
host.Run(researchAgent, CheckThePolicy);                                                // (3)
```

```python
gateway = PaymentGateway(GRANTS_BY_THE_GATEWAYS_ADMINISTRATOR)
host = AgentHost(gateway, credentials=Credentials.SHARED_KEY)                # (1)
payment_agent = PaymentAgent(tools=PaymentGateway.TOOLS)
research_agent = ResearchAgent(tools=["FetchPage", *PaymentGateway.TOOLS])   # (2)

host.run(payment_agent, PAY_THE_GARDEN_INN)
host.run(research_agent, CHECK_THE_POLICY)                                   # (3)
```

Both programs print the same output. The naive half:

```text
Naive: sign every agent's gateway calls with the one key in the team's config
  Payment agent asks for Charge(360 EUR, GARDEN-INN-001)
    runtime: signs with KEY-001, the account owner's key for every tool
    payment gateway: paid 360 EUR to GARDEN-INN-001 (CH-001)
  Research agent asks for FetchPage(Garden Inn cancellation policy)
    web: "Free cancellation up to 48 hours before arrival."
  Research agent asks for Refund(CH-001)
    runtime: signs with KEY-001, the account owner's key for every tool
    payment gateway: refunded 360 EUR on CH-001
  payment gateway log: CH-001 charged by the account owner; CH-001 refunded by the account owner
  Result: CH-001 refunded, 360 EUR, and the gateway's log names the account owner, not an agent
```

## Where it breaks

**(1) The runtime signs every agent's calls with the account owner's key.** KEY-001 came with the account:
every tool, no expiry, and none of the gateway administrator's grants apply to it. To the gateway, every
agent is the account owner.

**(2) Every agent reaches every gateway tool.** A connected server's tools are all exposed by default: in
[Microsoft Agent Framework's Python MCP tools](https://github.com/microsoft/agent-framework/blob/main/python/packages/core/agent_framework/_mcp.py),
an `allowed_tools` of `None` "exposes every tool advertised by the MCP server."

**(3) The Research agent's model refunds because it can.** It reads "can CH-001 be refunded?" as a
request, and the key says yes: trip-42's room is no longer paid for.

The fixed version still gives every agent every gateway tool, and the Research agent's model still asks
for the refund; what changes is that the runtime asks the gateway for each agent's own key, and the
gateway's administrator decides.

### How to avoid it

Ask as yourself; the resource decides. The runtime holds each agent's identity and, for each call, asks
the resource for a key scoped to that agent and that one tool, valid for minutes. The model never sees a
key, never picks an identity, and never asks for scopes. The grants belong to the gateway's administrator;
your team's tool lists stay as a second guard, on your side of the call. Each agent then makes only the
calls its grants allow, and the gateway's log names the agent: least privilege, plus Saltzer and
Schroeder's [least common mechanism](https://www.cs.virginia.edu/~evans/cs551/saltzer/) ("Minimize the
amount of mechanism common to more than one user"), applied per agent.

```csharp
var gateway = new PaymentGateway(grantsByTheGatewaysAdministrator);
var host = new AgentHost(gateway, credentials: Credentials.KeyPerAgent);   // never one key for all
var paymentAgent = new PaymentAgent(tools: PaymentGateway.Tools);
var researchAgent = new ResearchAgent(tools: ["FetchPage", .. PaymentGateway.Tools]);

if (!host.Run(paymentAgent, PayTheGardenInn))
{
    Console.WriteLine("  Result: nothing paid for trip-42;"
        + " only the gateway's administrator can grant Charge");
    return;
}

host.Run(researchAgent, CheckThePolicy);
PrintResult(gateway);
```

```python
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
```

And the fixed half of the output:

```text
Fixed: ask the gateway for each agent's own key, one tool at a time
  Payment agent asks for Charge(360 EUR, GARDEN-INN-001)
    runtime: asks the payment gateway for a payment-agent key for Charge
    payment gateway: issued KEY-002 to payment-agent, for Charge only
    payment gateway: paid 360 EUR to GARDEN-INN-001 (CH-001)
  Research agent asks for FetchPage(Garden Inn cancellation policy)
    web: "Free cancellation up to 48 hours before arrival."
  Research agent asks for Refund(CH-001)
    runtime: asks the payment gateway for a research-agent key for Refund
    payment gateway: refused; its administrator grants research-agent no Refund
  payment gateway log: CH-001 charged by payment-agent; Refund refused to research-agent
  Result: CH-001 stays paid; the gateway refused research-agent and logged it
```

Another administrator's "no" is final. If the gateway's administrator withdraws the Payment agent's grant,
the runtime calls nothing and the run ends with nothing paid; it never falls back to the shared key. Don't
turn the refusal into a consent prompt either: MCP lets a client acting on its own behalf, as each agent
does here, "abort the request immediately" instead of stepping up. And a wrong call within a grant still
goes through, such as the Payment agent refunding the wrong charge: bind Charge and Refund to the booking,
as in [fallacy 4](../04-network-is-secure/), or put the rule in the tool, as in
[fallacy 9](../09-logic-can-be-centralized/).

In real code, the per-agent key comes from the resource, in either language: an MCP token per agent (one
registered client each), an agent identity from your cloud, or, where the provider issues only long-lived
keys, a Stripe restricted key per agent. Check one default too: Microsoft Agent Framework's .NET samples
build every agent from one client with `DefaultAzureCredential`, which
[the framework's docs](https://learn.microsoft.com/en-us/agent-framework/get-started/your-first-agent) call
"convenient for development but requires careful consideration in production." If the managed identity
fails, `DefaultAzureCredential` can fall back to a developer's login, which "may fail or result in
unexpected elevation or reduction of privileges"
([Azure SDK](https://learn.microsoft.com/en-us/dotnet/azure/sdk/authentication/best-practices)). Use a
specific credential; even a managed identity belongs to the host, not to an agent.

The example leaves out two things. A key per agent isn't a limit per agent: OpenAI sets rate limits "at
the organization level and at the project level, not user level"
([OpenAI](https://developers.openai.com/api/docs/guides/rate-limits)), and budgets are
[fallacy 7](../07-transport-cost-is-zero/). And acting for the traveler calls for delegation, not
impersonation: when A impersonates B, A is "indistinguishable from B in that context"
([RFC 8693](https://www.rfc-editor.org/rfc/rfc8693#section-1.1)).

---

**Read more**

- [The Eight Fallacies of Distributed Computing](https://nighthacks.com/jag/res/Fallacies.html) (L. Peter
  Deutsch, on James Gosling's site)
- [Fallacy #6: There is one administrator](https://particular.net/blog/there-is-one-administrator) (David
  Boike, Particular Software, 2021)
- [Fallacies of Distributed Computing Explained](https://web.archive.org/web/20070204070434/http://www.rgoarchitects.com:80/Files/fallacies.pdf)
  (Arnon Rotem-Gal-Oz, archived PDF)
- [Model Context Protocol: Authorization](https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization)
  (specification, 2026-07-28 revision)
- Related: [The Fallacies of AI-Assisted Programming](https://writing.alteredcraft.com/p/the-fallacies-of-ai-assisted-programming)
  (Sam Keen, 2026), whose fallacy "The humans are the only admins" calls a coding agent *"itself an
  unaccountable administrator in your application environments."*

[← All ten fallacies](../../README.md)
