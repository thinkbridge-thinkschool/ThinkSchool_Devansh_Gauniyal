# Day 3, Task 3 — Lock down the API end to end

## 1. GitHub link / pull-request URL

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/pull/2

## 2. CI run URL

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/actions/runs/31584305491

## 3. Required mentor notes

CI run: https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/actions/runs/31584305491

PR review: this is solid

All required authentication and authorization integration tests passed in GitHub Actions, covering anonymous access (`401`), authenticated access with the wrong policy (`403`), authenticated access with the correct policy (`200`), expired access tokens (`401`), and revoked refresh-token chains (`401`).

The standalone Quotes API combines internal JWT and Entra JWT validation, refresh-token rotation with reuse detection, named authorization policies on every quote-domain mutation, and end-to-end integration tests through `WebApplicationFactory`.

The Entra integration tests use temporary test-only signing keys and static OpenID Connect metadata, so CI exercises the real issuer selector and JWT handler without contacting Microsoft or using live credentials. These fixtures do not replace the genuine Entra verification completed in Task 1. Refresh-token records are held in memory for this training project and store SHA-256 token hashes rather than raw refresh tokens.

Authentication endpoints are intentional quote-policy exceptions: `/api/auth/login` verifies the configured internal caller using a salted PBKDF2 password hash, while `/api/auth/refresh` validates and rotates the presented refresh token. Every quote-domain mutation has explicit authorization protection.

## 4. Required authentication test matrix

| Scenario | Verified result |
| --- | --- |
| Anonymous protected request | `401 Unauthorized` |
| Authenticated without `quotes.write` | `403 Forbidden` |
| Authenticated with `quotes.write` | `200 OK` |
| Expired access token | `401 Unauthorized` |
| Reused refresh token | `401 Unauthorized` |
| Descendant of revoked refresh family | `401 Unauthorized` |
| Malformed access token | `401 Unauthorized` |
| Valid internal JWT | `200 OK` |
| Valid Entra-style test JWT | `200 OK` |
| Authenticated quote owner delete | `200 OK` |
| Authenticated non-owner delete | `403 Forbidden` |

Mutating-endpoint audit:

| HTTP method | Route | Mutates data? | Applied protection | Verified statuses |
| --- | --- | --- | --- | --- |
| `POST` | `/api/quotes` | Yes | `can-edit-quotes` (`scope=quotes.write`) | `401`, `403`, `200` |
| `PUT` | `/api/quotes/{id}` | Yes | `can-edit-quotes` (`scope=quotes.write`) | `401`, `403`, `200` |
| `DELETE` | `/api/quotes/{id}` | Yes | Authentication plus imperative `can-delete-own-quote` resource policy | `401`, `403`, `200` |
| `POST` | `/api/auth/login` | Security endpoint | PBKDF2 credential validation; intentional quote-policy exception | Successful token pair exercised |
| `POST` | `/api/auth/refresh` | Security endpoint | Single-use refresh-token validation and atomic rotation; intentional quote-policy exception | `200`, reuse `401`, revoked descendant `401` |

## 5. Local test command and genuine result

Working directory:

```text
day-3/task-3
```

Commands:

```text
dotnet restore Task3.slnx
dotnet build Task3.slnx --no-restore --disable-build-servers --verbosity minimal
dotnet test Task3.slnx --no-build --disable-build-servers --verbosity normal
```

Genuine result before push:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)

Test Run Successful.
Total tests: 19
     Passed: 19
 Total time: 2.0043 Seconds
```

## 6. CI test result

Direct run: https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/actions/runs/31584305491

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)

Test Run Successful.
Total tests: 19
     Passed: 19
```

## 7. PR review line

this is solid

## 8. What did you learn this session?

I learned how authentication, authorization policies, and refresh-token security work together across the complete API request flow. I also learned how `WebApplicationFactory` and CI can verify that behavior without live credentials.

## 9. What would break this?

Incorrect issuer, audience, or policy configuration could reject valid users or permit the wrong operation. A non-atomic refresh-token rotation process could also allow token reuse or fail to revoke the entire compromised token family.
