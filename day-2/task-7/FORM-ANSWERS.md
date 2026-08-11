# Thinkbridge Day 2, Task 7 Form Answers

## What did you learn this session?

I learned that refresh-token rotation makes every refresh token single-use. Storing only token hashes and revoking the replacement chain when an old token is reused limits the damage if a token is leaked.

## What would break this?

A race condition could allow two requests to refresh the same token if validation and replacement are not atomic. The revoke-and-replace operation must therefore run transactionally.
