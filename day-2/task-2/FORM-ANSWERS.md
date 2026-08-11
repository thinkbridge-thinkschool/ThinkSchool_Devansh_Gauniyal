# Thinkbridge Day 2, Task 2 Form Answers

## GitHub link

https://github.com/devansh-gauniyal/thinkschool/tree/main/day-2/task-2

## Mentor notes

Use the full contents of `MENTOR-NOTES.txt`.

## What did you learn this session?

I learned that cancellation is cooperative: accepting a token at the HTTP boundary is not enough unless the same token reaches every awaited service, repository, and EF Core operation. A start signal in the test makes mid-request cancellation deterministic instead of relying on timing.

## What would break this?

Dropping the token in any layer or omitting it from an EF Core call could let database work complete after the client disconnects. The integration test would fail or reach its safety timeout if the repository stopped receiving the request's cancellable token.
