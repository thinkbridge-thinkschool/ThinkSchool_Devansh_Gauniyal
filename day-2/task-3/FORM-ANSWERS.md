# Thinkbridge Day 2, Task 3 Form Answers

## GitHub link

https://github.com/devansh-gauniyal/thinkschool/tree/main/day-2/task-3

## What did you learn this session?

I learned that aggregate invariants are ideal unit-test targets because they can be verified directly with pure, fast tests. Arrange-act-assert and Fluent Assertions make each business rule clear without database or framework setup.

## What would break this?

Allowing callers to bypass `Collection` methods or changing a rule without updating its focused test could let invalid state into the aggregate. Using real time or shared fixtures would also make this otherwise deterministic suite less reliable.
