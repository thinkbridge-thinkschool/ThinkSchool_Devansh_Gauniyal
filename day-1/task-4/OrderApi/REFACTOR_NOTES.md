# Order controller refactor notes

## Scope and preservation

The original AI-generated controller is preserved unchanged at `Legacy/OrderController.cs`, next to the prompt that generated it. It is excluded from compilation so the refactored `OrdersController` can own `POST /api/orders` without an ambiguous route. The observations below were recorded before the refactor was implemented.

## Review findings

### 1. God controller / giant action

- **Problem:** `CreateOrder` owns request parsing, validation, discounts, tax, shipping, EF queries, persistence, logging, and response construction.
- **Why harmful:** Unrelated reasons to change accumulate in one method, increasing regression risk and making the control flow difficult to understand or review.
- **Intended fix:** Keep HTTP concerns in `OrdersController`, move business rules into `OrderService`, and isolate persistence in `OrderRepository`.

### 2. Business logic in the HTTP layer

- **Problem:** Discount thresholds, returning-customer discounts, tax, shipping, order status, and totals are calculated in the controller.
- **Why harmful:** The rules cannot be reused by a background worker or another transport without duplicating controller code, and they are difficult to unit test.
- **Intended fix:** Put deterministic order processing in `OrderService` and unit-test it independently of ASP.NET Core.

### 3. Direct `DbContext` access

- **Problem:** The controller queries `OrderDbContext`, adds an entity, and calls `SaveChanges` itself.
- **Why harmful:** HTTP behavior is tightly coupled to EF Core and a concrete database, preventing focused tests and making persistence changes ripple into the API layer.
- **Intended fix:** Depend on `IOrderService` in the controller and `IOrderRepository` in the service; let `OrderRepository` be the only layer that uses the context.

### 4. Synchronous EF Core calls in an async action

- **Problem:** `ToList()` and `SaveChanges()` block threads even though the method is declared `async`.
- **Why harmful:** Under load, blocked request threads reduce throughput and can lead to thread-pool starvation. The `Task.Delay(1)` does not make database access asynchronous.
- **Intended fix:** Use `AddAsync` and `SaveChangesAsync`, and await each operation through every layer.

### 5. Missing cancellation propagation

- **Problem:** The action does not accept a `CancellationToken`, and database work continues after the client disconnects.
- **Why harmful:** Abandoned requests waste database connections and server capacity.
- **Intended fix:** Accept the request cancellation token at the controller boundary and pass it through service and repository methods to EF Core.

### 6. Empty catch blocks

- **Problem:** Four `catch { }` blocks suppress coupon, configuration, note-processing, and database exceptions.
- **Why harmful:** Failures become invisible, execution continues with partial or corrupt state, and operators lose the original exception and stack trace.
- **Intended fix:** Remove unnecessary catches. Catch only `DbUpdateException` at the service boundary, log it with context, preserve it as an inner exception, and map the domain-specific failure to HTTP 503.

### 7. Anonymous and inconsistent responses

- **Problem:** The action returns `Task<object>` with unrelated anonymous shapes for validation, server errors, and success.
- **Why harmful:** The response contract is not visible to OpenAPI or compile-time checks, and clients cannot rely on a stable schema.
- **Intended fix:** Return `ActionResult<OrderResponse>` and standard ASP.NET Core `ValidationProblemDetails` / `ProblemDetails` for failures.

### 8. Weak and mixed validation

- **Problem:** Shape checks, business validation, normalization, and calculations are interleaved. Email validation only looks for `@`, while zero quantity and zero price are accepted.
- **Why harmful:** Invalid data reaches calculations and persistence, and adding one rule requires editing fragile orchestration code.
- **Intended fix:** Put request-shape constraints on `CreateOrderRequest` with data annotations, and enforce business invariants in one service validation method.

### 9. Null-dereference risks

- **Problem:** The action dereferences `request`, its string properties, `Notes`, and individual note values before establishing that they are non-null.
- **Why harmful:** Malformed JSON can turn an expected 400 response into an unhelpful 500 error.
- **Intended fix:** Use a nullable, annotated request DTO handled by `[ApiController]`, then verify required values before normalization in the service.

### 10. Off-by-one defects

- **Problem:** Bulk discount uses `Quantity > 10` rather than the intended threshold of 10, and the notes loop uses `index <= Count`.
- **Why harmful:** Boundary orders receive the wrong price and every non-null notes list eventually accesses outside its bounds.
- **Intended fix:** Express the threshold as a named constant with `>=`; remove unrelated note mutation from order creation.

### 11. Persistence failure can be reported as success or lose context

- **Problem:** `SaveChanges` failures are swallowed. The later `Id == 0` check guesses whether persistence worked and returns a generic anonymous error.
- **Why harmful:** The true cause is lost, behavior depends on key-generation details, and clients receive inconsistent failure semantics.
- **Intended fix:** Allow successful repository calls to return the saved entity; translate logged `DbUpdateException` into `OrderPersistenceException`, then return a standard 503 response.

### 12. Mutable transport input

- **Problem:** The controller truncates and replaces items in `request.Notes`.
- **Why harmful:** Input mutation creates surprising side effects and makes later logic depend on execution order.
- **Intended fix:** Use immutable-style DTO properties and map validated input into a new domain entity without mutating the request.

### 13. Time and culture inconsistencies

- **Problem:** The code mixes `DateTime.Now`, `DateTimeOffset.Now`, culture-sensitive `decimal.Parse`, and repeated clock reads.
- **Why harmful:** Results vary by host locale and time zone, and elapsed-time fields can be inconsistent.
- **Intended fix:** Persist UTC timestamps, use typed configuration where needed, and keep operational timing out of the response contract.

### 14. Magic numbers and strings

- **Problem:** Thresholds, discount rates, tax, shipping fees, coupon codes, status values, and maximum sizes are scattered literals.
- **Why harmful:** Rules are hard to discover and a policy change can leave inconsistent values behind.
- **Intended fix:** Give business thresholds named constants (or typed configuration when externally configurable) and centralize rule evaluation in the service.

### 15. Domain entity leakage and overexposed response

- **Problem:** Persistence-derived values and internal calculation details are manually assembled alongside transport metadata in one anonymous object.
- **Why harmful:** Database/domain changes silently alter the public API, and sensitive fields could be exposed accidentally.
- **Intended fix:** Map only approved fields from `Order` into an explicit `OrderResponse` DTO.

### 16. Poor dependency direction and testability

- **Problem:** The controller depends on concrete configuration and EF Core behavior, and the original has zero tests.
- **Why harmful:** Tests require too much infrastructure, so boundary cases and failure paths are likely to regress.
- **Intended fix:** Register interfaces with dependency injection, add focused service unit tests for success, validation, and repository failure, plus a `WebApplicationFactory` endpoint test.

## Resulting architecture

The refactor follows a one-way flow: `OrdersController` → `IOrderService` → `IOrderRepository` → `OrderDbContext`. DTOs define the public boundary, the model defines persisted state, and the controller maps known application failures to appropriate HTTP responses.
