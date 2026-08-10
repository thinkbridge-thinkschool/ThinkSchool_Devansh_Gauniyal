# Day 1 Task 5 — AI-assisted order rule refactor

## Selected Task 4 code

I selected `OrderService.ValidateBusinessRules` from Task 4. The method checked required fields, quantity, and price in one place. Adding another validation rule meant changing the service.

## Refactor

Task 5 copies only the order-processing slice needed for this exercise. `OrderService` now receives a list of `IOrderRule` strategies and runs them before processing an order. Required fields, quantity, and price each have a small rule class. A new rule can be implemented and added to the list without editing `OrderService`.

The discount calculation remains in `OrderService` because this exercise is about validation strategies, not redesigning the whole Task 4 application.

## Run locally

```bash
dotnet build Task5.sln
dotnet test Task5.sln
```
