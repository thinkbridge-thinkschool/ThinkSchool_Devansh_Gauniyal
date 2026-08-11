The original Quote model was anemic because public setters allowed a caller to assign any author or text. Validation in an endpoint protected only that endpoint. An import job could bypass those checks and save an invalid quote.

The rich model gives Quote responsibility for its own rules. Quote.Create is the single public entry point for construction. It checks that Author contains 1–200 characters and Text contains 1–1000 characters, rejecting null, empty, whitespace-only, or oversized values with a domain error. Controllers, jobs, and services therefore receive identical validation without duplicating rules.

Private setters keep the entity compatible with EF Core while preventing callers from rewriting state. Text has no public setter or update method, so published wording cannot change. SoftDelete is also a domain operation. Callers ask the quote to delete itself instead of directly setting a flag or physically removing its row. A global query filter keeps deleted quotes out of normal reads.

For example, a CSV importer might create a quote with 1,001 text characters because it forgot controller validation, then later overwrite Text after publication. Now the importer must call Quote.Create, which returns a domain error for that input, and immutable Text prevents the second bug completely.
