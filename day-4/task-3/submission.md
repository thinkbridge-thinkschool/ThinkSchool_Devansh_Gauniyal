## Selected answer

The distributed trace for that specific request

## What did you learn this session?

The three pillars aren't interchangeable — each one is bad at a specific shape of question, so the real skill is matching the question to the tool rather than reaching for whichever one is most familiar. Metrics are cheap specifically because they throw away individual events, which is exactly why they can't answer a single-customer question like this one. Traces are the only pillar that preserves the internal call structure of one request, which is why they're the only real answer when you need to know which specific operation was slow.

## What would break this?

The trace is only as good as the instrumentation behind it — if the database client isn't instrumented, there's no query span at all, just unexplained gap time sitting inside the parent span. Sampling is the other failure mode: if traces are sampled at a low rate, this specific 11:42 request may simply never have been recorded, so the right tool is picked but the data isn't there. And without a correlation ID propagated across services, there'd be no way to connect the customer's report to the right trace in the first place.
