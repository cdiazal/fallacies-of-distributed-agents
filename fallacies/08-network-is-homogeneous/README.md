# 8. The network is homogeneous

> *"The network is homogeneous."* — the eighth of the eight fallacies of distributed computing, from L.
> Peter Deutsch and colleagues at Sun Microsystems.

## The fallacy

A network mixes machines, operating systems, languages, and vendors; a homogeneous one, as Arnon
Rotem-Gal-Oz put it, is "the exception, not the rule." David Boike puts the hard part in the meaning, not
the wires: "The true challenge of non-homogenous networks lies in semantic interoperability." Dates are
the classic case: "Date and time formats cause a lot of confusion and interoperability problems on the
Internet" ([RFC 3339](https://www.rfc-editor.org/rfc/rfc3339)).

Believing the network is homogeneous means trusting that the other side writes data the way you read it.
A booking service in Madrid writes 01/03/2027, and a billing service with US settings reads January 3:
.NET's `DateTime.Parse` is "culture-sensitive unless the string to be parsed" follows ISO 8601
([Microsoft](https://learn.microsoft.com/en-us/dotnet/api/system.datetime.parse)). Nothing fails, because
the wrong date is still a valid date.

The classic answer is standards and contracts, not sameness. "Do not rely on proprietary protocols,"
Rotem-Gal-Oz advises; write dates one standard way, put a
[Message Translator](https://www.enterpriseintegrationpatterns.com/patterns/messaging/MessageTranslator.html)
at each boundary (Gregor Hohpe and Bobby Woolf), and turn what each consumer expects into
[consumer-driven contracts](https://martinfowler.com/articles/consumerDrivenContracts.html) that the
provider runs as tests (Ian Robinson, 2006).

A sender's format lives in its code: it writes dates the same way on every call and changes only with a
release, which is where the contract test runs.

## The picture

![Left: a booking service in Madrid sends 01/03/2027, and a billing service with US settings, using
DateTime.Parse, reads January 3; the fix is one standard format (ISO 8601), a translator at each
boundary, and a contract test in every build. Right: the runtime sends the Flight agent's model calls to
provider B, the cheaper of two providers serving the same model; provider B ignores the reply schema's
"pattern," so the model writes FL101's arrival as 01/03/2027, the runtime passes it to the Hotel agent,
whose model reads it month first, and BookHotel(Garden Inn, 2027-01-03, 2 nights) books B-001 for January
3-5 while FL101 lands March 1; the fix is a runtime that parses every reply into a date and asks the next
provider when one fails, plus contract tests for each model, provider, and version.](diagram.excalidraw.svg)

## With many agents

In a multi-agent system, the format of a reply lives in a model, not in code. Providers and frameworks
already ship the classic answer: structured outputs take a JSON Schema and constrain the model while it
writes ([OpenAI](https://developers.openai.com/api/docs/guides/structured-outputs)), and frameworks
generate that schema from your types. A reply in the right shape looks checked, and that's the trap.

Two things are new. First, whoever serves the model enforces the schema, each provider enforces a
different part, and who serves it can change without a release. OpenAI's strict mode refuses a schema it
can't enforce: "you will receive an error." Google's Agent Platform "can still handle your request but
ignores the field," and `pattern` isn't on its list
([Google](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/capabilities/control-generated-output)).
In November 2025, the same Kimi K2 model's tool calls matched their schema 100.00% of the time on Moonshot
AI's API and 71.96% on Together, reached through OpenRouter
([K2 Vendor Verifier](https://github.com/MoonshotAI/K2-Vendor-Verifier)), whose default is to "load
balance requests across providers, prioritizing price"
([OpenRouter](https://openrouter.ai/docs/guides/routing/provider-selection)). Second, the next reader is
a model, and you can't make it strict: it has no parse step that can fail. Given 01/03/2027, it picks a
reading, which is what [RFC 9413](https://www.rfc-editor.org/rfc/rfc9413) warns against: an ambiguous
message "processed according to the inferred intent of the sender."

That's why the classic answer, applied well, still fails here. Here, the schema is standard and the
airline's confirmation is typed, yet the same model, schema, and confirmation give 2027-03-01 through
provider A and 01/03/2027 through provider B, with no release in between. On 132 days of 2027, the naive
version books the wrong month without an error. Microsoft.Extensions.AI admits it: "Language models are
not guaranteed to honor the requested schema"
([source](https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/ChatResponse%7BT%7D.cs)).

## In code

Both programs simulate the [example system](../../docs/example-system.md) with one change: two providers
serve the Flight agent's model, and the runtime sends its calls to the cheaper one, provider B. Both
providers accept the reply schema, whose `pattern` requires a YYYY-MM-DD arrival; only provider A
enforces it. The plan for trip-42 has two steps: the Flight agent books FL101, then the Hotel agent books
two nights at the Garden Inn from the day FL101 lands, which the runtime takes from the Flight agent's
reply. The Flight agent's model writes that date day first unless the provider allows only YYYY-MM-DD;
the Hotel agent's model reads a slashed date month first when it can. Each program has no dependencies
and runs a naive version, then a fixed one. From this folder:

```text
dotnet run dotnet/NetworkIsHomogeneous.cs      # .NET 10
python python/network_is_homogeneous.py        # Python 3.12+
```

The naive runtime passes on the Flight agent's reply as the provider returns it
([full .NET file](dotnet/NetworkIsHomogeneous.cs), [full Python file](python/network_is_homogeneous.py)):

```csharp
var (airline, hotels) = (new Airline(), new Hotels());
var runtime = new AgentRuntime(airline, hotels, contract: null);           // (1)
Provider[] byPrice = [new("provider B", Enforces: ["type", "required"]),   // (2)
                      new("provider A", Enforces: ["type", "required", "pattern"])];
var hotelAgent = new HotelAgent();                                         // (3)

if (runtime.RunFlightStep(new FlightAgent(), byPrice, BookTheFlight) is { } arrival)
    runtime.RunHotelStep(hotelAgent, arrival);
PrintResult(airline, hotels);
```

```python
airline, hotels = Airline(), Hotels()
runtime = AgentRuntime(airline, hotels, contract=None)               # (1)
by_price = [Provider("provider B", enforces=("type", "required")),   # (2)
            Provider("provider A", enforces=("type", "required", "pattern"))]
hotel_agent = HotelAgent()                                           # (3)

if (arrival := runtime.run_flight_step(FlightAgent(), by_price, BOOK_THE_FLIGHT)) is not None:
    runtime.run_hotel_step(hotel_agent, arrival)
print_result(airline, hotels)
```

Both programs print the same output. The naive half:

```text
Naive: route the Flight agent's model to the cheaper provider, and pass on what it replies
  runtime: sends the Flight agent's model calls to provider B, the cheaper of the two
  Flight agent asks for BookFlight(FL101)
    airline: booked FL101, Madrid 07:10 to London 08:35, lands March 1, 2027
  Flight agent replies through provider B: {"flight": "FL101", "arrival": "01/03/2027"}
    provider B: accepted the schema but ignored its "pattern", no error
  runtime: hands step-2 to the Hotel agent: "Book the Garden Inn for 2 nights from 01/03/2027."
  Hotel agent asks for BookHotel(Garden Inn, 2027-01-03, 2 nights)
    hotels: booked B-001 (Garden Inn, January 3-5, 360 EUR)
  Result: FL101 lands on March 1, 2027, and the Garden Inn is booked for January 3-5 (B-001)
```

## Where it breaks

**(1) The runtime passes on the Flight agent's reply as the provider returns it.** Nothing on your side
reads the arrival before the Hotel agent's model does. The schema went to the provider, and the check
went with it.

**(2) Provider B, the cheaper one, ignores the schema's `pattern`.** It accepts the schema with no error,
so the model writes the date its own way.

**(3) The Hotel agent's model reads 01/03/2027 month first.** It has no parse step that can fail, so it
books January 3-5 with a valid date that BookHotel accepts.

The fixed version still sends the Flight agent's calls to provider B first, and the Hotel agent's model
still reads slashed dates month first; what changes is that the runtime parses every reply itself and,
when one doesn't parse, asks the next provider.

### How to avoid it

Parse on your side, not theirs. The runtime owns each agent's reply contract and parses every reply into
typed values before any model reads it. What doesn't parse is refused, never guessed, and the runtime asks
the next provider for a new reply; it never books FL101 again. The models and providers stay as they are
in the naive version, on purpose: no model judges its own reply or picks its provider, and the schema
sent to a provider stays a request. It's a
[Message Translator](https://www.enterpriseintegrationpatterns.com/patterns/messaging/MessageTranslator.html)
at the edge that accepts one canonical form and refuses the rest: the "virtuous intolerance" of
[RFC 9413](https://www.rfc-editor.org/rfc/rfc9413), applied to the fields that drive an action.

```csharp
var (airline, hotels) = (new Airline(), new Hotels());
var runtime = new AgentRuntime(airline, hotels, contract: new ReplyContract());   // on our side
Provider[] byPrice = [new("provider B", Enforces: ["type", "required"]),
                      new("provider A", Enforces: ["type", "required", "pattern"])];
var hotelAgent = new HotelAgent();

if (runtime.RunFlightStep(new FlightAgent(), byPrice, BookTheFlight) is not { } arrival)
{
    Console.WriteLine("  Result: FL101 booked, but no hotel for trip-42;"
        + " no provider's reply met the Flight agent's contract");
    return;
}

runtime.RunHotelStep(hotelAgent, arrival);
PrintResult(airline, hotels);
```

```python
airline, hotels = Airline(), Hotels()
runtime = AgentRuntime(airline, hotels, contract=ReplyContract())   # on our side
by_price = [Provider("provider B", enforces=("type", "required")),
            Provider("provider A", enforces=("type", "required", "pattern"))]
hotel_agent = HotelAgent()

if (arrival := runtime.run_flight_step(FlightAgent(), by_price, BOOK_THE_FLIGHT)) is None:
    print("  Result: FL101 booked, but no hotel for trip-42;"
          " no provider's reply met the Flight agent's contract")
    return

runtime.run_hotel_step(hotel_agent, arrival)
print_result(airline, hotels)
```

And the fixed half of the output:

```text
Fixed: parse every reply on our side, and ask the next provider when one fails
  runtime: sends the Flight agent's model calls to provider B, the cheaper of the two
  Flight agent asks for BookFlight(FL101)
    airline: booked FL101, Madrid 07:10 to London 08:35, lands March 1, 2027
  Flight agent replies through provider B: {"flight": "FL101", "arrival": "01/03/2027"}
    provider B: accepted the schema but ignored its "pattern", no error
    runtime: refused the reply: arrival "01/03/2027" isn't a YYYY-MM-DD date; asks provider A for the reply
  Flight agent replies through provider A: {"flight": "FL101", "arrival": "2027-03-01"}
    provider A: accepted the schema and enforced its "pattern"
    runtime: parsed arrival as March 1, 2027
  runtime: hands step-2 to the Hotel agent: "Book the Garden Inn for 2 nights from 2027-03-01."
  Hotel agent asks for BookHotel(Garden Inn, 2027-03-01, 2 nights)
    hotels: booked B-001 (Garden Inn, March 1-3, 360 EUR)
  Result: FL101 lands on March 1, 2027, and the Garden Inn is booked for March 1-3 (B-001)
```

If no provider's reply parses, FL101 stays booked, no hotel is booked, and the traveler is told; undoing
FL101 is [fallacy 10](../10-system-is-monolithic/). Never fall back to the raw reply or to an untested
provider. Move the refusal into your build: run each agent's contract as consumer-driven contract tests,
with known answers and many runs each, against every model, provider, and version you route to; route
only to what passed them all. They also answer [fallacy 5](../05-topology-doesnt-change/): a changed
tool definition or reply schema is compatible when all consumers' tests pass. Pin snapshots,
which "lock in a specific version of the model" ([OpenAI](https://developers.openai.com/api/docs/models/gpt-4o)),
and keep the runtime check anyway: a passing test is only a sample, and infrastructure updates can
cause "minor differences in observable behavior"
([Anthropic](https://platform.claude.com/docs/en/about-claude/models/model-ids-and-versions)).

In real code, parse where one agent's reply becomes the next step's input. In .NET, ask for
`GetResponseAsync<FlightReply>` with a `DateOnly` arrival: System.Text.Json reads only `yyyy-MM-dd`, so
`Result` throws on anything else and `TryGetResult` returns `false`. In Python, use a Pydantic `date`
field, which rejects 01/03/2027
([Pydantic](https://pydantic.dev/docs/validation/latest/api/pydantic/standard_library_types/#datetimedate)),
through the OpenAI SDK's `parse`, and treat Pydantic's `ValidationError`, or a `parsed` of `None`, as a
failure. Check one default too: Microsoft.Extensions.AI's OpenAI adapter moves `pattern` and `format`
from a JSON response schema into its description, so `DateOnly`'s `"format": "date"` reaches the
provider as a hint
([source](https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIClientExtensions.cs)).

The example leaves out two things. A wrong value in the right form, 2027-01-03, still parses: put the
rule that ties check-in to the landing day in BookHotel, as in [fallacy 9](../09-logic-can-be-centralized/).
And the runtime fills step-2 from the agent's reply, though it holds the airline's typed confirmation;
where code can reach the record, build the next step from it, as in [fallacy 4](../04-network-is-secure/).

---

**Read more**

- [The Eight Fallacies of Distributed Computing](https://nighthacks.com/jag/res/Fallacies.html) (L. Peter
  Deutsch, on James Gosling's site)
- [Fallacy #8: The network is homogeneous](https://particular.net/blog/the-network-is-homogeneous) (David
  Boike, Particular Software, 2021)
- [Fallacies of Distributed Computing Explained](https://web.archive.org/web/20070204070434/http://www.rgoarchitects.com:80/Files/fallacies.pdf)
  (Arnon Rotem-Gal-Oz, archived PDF)
- [RFC 9413: Maintaining Robust Protocols](https://www.rfc-editor.org/rfc/rfc9413) (Martin Thomson and
  David Schinazi, 2023)
- Related: [The Fallacies of AI-Assisted Programming](https://writing.alteredcraft.com/p/the-fallacies-of-ai-assisted-programming)
  (Sam Keen, 2026), whose sixth fallacy is *"All models are interchangeable,"* and
  [Lesson 147](https://www.developertoarchitect.com/lessons/lesson147.html) (Mark Richards, 2022), which
  names "versioning is simple" as a fallacy of its own.
- Related: [AI Agents and fallacies of Distributed Computing](https://blog.gopenai.com/ai-agents-and-fallacies-of-distributed-computing-e45fadd1c765)
  (Krishna Avva, 2026), whose section on this fallacy is titled *"the weakest dependency sets the rules."*

[← All ten fallacies](../../README.md)
