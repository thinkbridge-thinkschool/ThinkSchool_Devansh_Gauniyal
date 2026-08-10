# AI reflection

Codex correctly identified `ValidateBusinessRules` as the part of Task 4 that would become harder to maintain as more checks were added. It moved the required-field, quantity, and price checks into small classes that all implement `IOrderRule`. The useful part is that `OrderService` now loops over the supplied rules instead of knowing each rule by name. It also kept the existing quantity range, maximum price, text normalization, and the 10-item discount boundary unchanged.

I would catch an AI-introduced bug by comparing each extracted rule with the original `if` statements and then checking boundary tests. The quantity rule is especially important because changing `< 1` to `< 0`, or `> 100` to `>= 100`, would silently accept or reject the wrong orders. The successful test also checks that exactly 10 items still receives the discount.

The Copilot-style comments saved time by turning three plain-English cases into clear test names, setup, and assertions. I still reviewed the assertions because a generated test can pass while checking the wrong error message or avoiding the boundary value.

One idea I rejected was creating a separate interface and result type for every rule outcome. That added ceremony without helping this small exercise. Returning a nullable error message from each rule is easier to follow here, although a larger system might need structured errors.

At 2 AM during a production issue, I would reach for Codex first because it can inspect the service, strategies, tests, and recent diff together. I would use it to narrow the failure and suggest checks, but I would verify logs and reproduce the problem before applying a change.
