# Thinkbridge Day 2, Task 4 Form Answers

## GitHub link

https://github.com/devansh-gauniyal/thinkschool/tree/day-2/task-4/day-2/task-4

## Mentor notes

Use the complete contents of `MENTOR-NOTES.txt`.

## What did you learn this session?

I learned that a rich entity gives every caller the same validation and protects important state transitions. A small factory result can represent expected domain errors clearly without exceptions or a large framework.

## What would break this?

Adding another public constructor, setter, or text-update method would let callers bypass the invariants. Removing the global query filter could also expose quotes that were correctly soft-deleted.
