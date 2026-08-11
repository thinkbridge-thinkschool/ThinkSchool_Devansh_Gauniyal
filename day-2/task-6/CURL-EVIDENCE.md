# Day 2, Task 6 — Live curl evidence

Executed against the real API on August 11, 2026 using a temporary SQLite database, a runtime-generated 256-bit signing key, and a runtime-generated development password. The temporary access-token lifetime was intentionally set to 2 seconds so the expired-token response could be exercised without fabricating a token or response. The normal configured and tested lifetime is 900 seconds.

Credentials, access tokens, refresh tokens, and signing keys are redacted. The API was shut down and the temporary files were removed after these requests.

## Login

```bash
curl --silent --request POST "http://127.0.0.1:5196/api/auth/login" \
  --header "Content-Type: application/json" \
  --data '{"email":"mentor.verification@example.test","password":"[REDACTED]"}'
```

Actual sanitized response:

```json
{"access_token":"[REDACTED]","refresh_token":"[REDACTED]","expires_in":2}
```

## 1. POST without a token

```bash
curl --silent --include --request POST "http://127.0.0.1:5196/api/quotes" \
  --header "Content-Type: application/json" \
  --data '{"author":"Live verifier","text":"Authentication is required."}'
```

Actual response:

```http
HTTP/1.1 401 Unauthorized
Content-Length: 0
Date: Tue, 11 Aug 2026 07:20:51 GMT
Server: Kestrel
WWW-Authenticate: Bearer

```

Response body: empty.

## 2. POST with a valid token

`$ACCESS_TOKEN` contained the fresh access token returned by the login request; its value is not committed.

```bash
curl --silent --include --request POST "http://127.0.0.1:5196/api/quotes" \
  --header "Content-Type: application/json" \
  --header "Authorization: Bearer $ACCESS_TOKEN" \
  --data '{"author":"Live verifier","text":"A valid token reaches the protected endpoint."}'
```

Actual response:

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Date: Tue, 11 Aug 2026 07:20:51 GMT
Server: Kestrel
Transfer-Encoding: chunked

{"id":1,"author":"Live verifier","text":"A valid token reaches the protected endpoint.","createdAtUtc":"2026-08-11T07:20:51.92996+00:00","isDeleted":false}
```

## 3. POST with an expired token

The same correctly signed access token was used after waiting 3 seconds, beyond its intentional 2-second live-verification lifetime. `$EXPIRED_TOKEN` represents that now-expired token; its value is not committed.

```bash
curl --silent --include --request POST "http://127.0.0.1:5196/api/quotes" \
  --header "Content-Type: application/json" \
  --header "Authorization: Bearer $EXPIRED_TOKEN" \
  --data '{"author":"Live verifier","text":"An expired token must be rejected."}'
```

Actual response:

```http
HTTP/1.1 401 Unauthorized
Content-Length: 0
Date: Tue, 11 Aug 2026 07:20:54 GMT
Server: Kestrel
WWW-Authenticate: Bearer error="invalid_token", error_description="The token expired at '08/11/2026 07:20:53'"

```

Response body: empty.
