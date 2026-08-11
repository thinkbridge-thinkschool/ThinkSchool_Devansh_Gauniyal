# Thinkbridge Day 2, Task 6 Form Answers

## GitHub link

https://github.com/devansh-gauniyal/thinkschool/tree/main/day-2/task-6

## Mentor notes

Use the complete contents of `MENTOR-NOTES.txt`.

## What did you learn this session?

I learned that issuing a JWT is only one part of authentication: the API must also validate its signature, issuer, audience, algorithm, and expiration on every protected request. Keeping signing keys and development credentials in runtime configuration makes secure failure the default without committing secrets.

## What would break this?

A leaked signing key would let an attacker mint accepted tokens, and non-normalized email storage could create duplicate identities. The temporary refresh token also has no persistence or rotation yet, so treating it as a complete refresh-token system would be unsafe.
