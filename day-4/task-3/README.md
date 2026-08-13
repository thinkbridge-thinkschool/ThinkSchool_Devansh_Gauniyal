# Day 4 — Task 3: Logs, metrics, traces: what each is for

## The question

A customer reports their API request was slow at 11:42 IST. You need to find which database
query in their request chain took the longest. What do you reach for?

- The metrics dashboard's p99 latency chart
- The log aggregator filtered to that timestamp
- The distributed trace for that specific request
- The container CPU usage chart

## Chosen answer

**The distributed trace for that specific request.**

## Why the trace is correct

The question is about **one** specific request and **one** specific operation inside it — not
"how slow are requests in general" but "where did the time go for this exact call." A trace
records the path that single request took across every service it touched. Each unit of work
along that path — an HTTP handler, a downstream call, a database query — is a span, and spans
nest to show the parent-child call structure (the API handler span contains the database-query
span, which might contain a connection-acquisition span, and so on). Every span carries its own
start time and duration. So the way to answer this question is: open the trace for that specific
request, walk down through its spans, and read off which child span's duration accounts for most
of the total. If the database query is the slow part, its span will show a duration close to the
overall request duration, and its position in the tree tells you exactly which operation it was.

No other pillar preserves that per-request, per-operation structure.

## Why each other option fails

- **The metrics dashboard's p99 latency chart** — metrics are pre-aggregated numbers over time
  (counters, gauges, histograms). By the time a number reaches a p99 chart, the individual
  request that produced it has already been thrown away — that's *why* metrics are cheap to
  store and query. A p99 chart can tell you "1% of requests are slow," which is a population-level
  statement, but it cannot single out this one customer's 11:42 request or tell you which query
  inside it was responsible. It answers "how bad is it in aggregate," not "what happened here."

- **The log aggregator filtered to that timestamp** — logs are discrete, free-text events.
  Filtering to 11:42 narrows down *which* events happened in that window, but logs carry no
  built-in notion of duration or nesting unless something explicitly logged a start time, an end
  time, and enough correlating detail to prove those two log lines belong to the same query on
  the same request. Even with well-structured logs, you'd be manually reconstructing what a trace
  already gives you for free: a parent-child call tree with per-span timing. Unless every query
  is explicitly instrumented to log its own duration, logs simply have no timing to attribute to
  a specific operation.

- **The container CPU usage chart** — this is infrastructure resource telemetry: it answers "is
  the host under CPU pressure," a completely different question from "which operation inside this
  request was slow." A slow database query is very often *not* a CPU problem at all (it could be
  waiting on a lock, a slow disk, or network latency to the DB) — the CPU chart wouldn't show
  that, and even if the host were CPU-saturated, the chart can't attribute that to one customer's
  one request.

## The three pillars, in my own words

- **Logs** are for "what happened during this specific request" — discrete events, high
  cardinality, good for reading a narrative of one thing at a time. Bad for counting how often
  something happens, because scanning every log line to count is expensive.
- **Metrics** are for "how many, how fast, in aggregate" — numbers over time, cheap precisely
  because individual events are discarded and only pre-aggregated summaries are kept. Bad for
  reconstructing what one specific user or request was doing, because that detail is gone.
- **Traces** are for "where did the time go, and where did the error originate, across services"
  — they preserve the structure of one request as it crosses service boundaries. Bad for
  aggregate questions ("how often does this happen across all requests"), since a trace is
  inherently about one request at a time.

Correlation IDs are the glue: a trace ID stamped on every log line and every metric attribute for
the same request is what lets you pivot from "I see this trace ID in a trace" to "now show me the
logs and metrics for that same ID" — without it, the three pillars are three separate, unrelated
data sets instead of three views of the same event.

## One practical note: this only works if it's instrumented

A trace only shows a database-query span if the data-access layer is actually instrumented — for
example, via OpenTelemetry auto-instrumentation for the specific DB client library in use. If that
dependency isn't instrumented, the trace doesn't show an error or a missing span; it just shows
unexplained gap time inside the parent span, with no child span to point at. The right tool being
"distributed trace" doesn't help if nothing ever recorded a span for that operation.
