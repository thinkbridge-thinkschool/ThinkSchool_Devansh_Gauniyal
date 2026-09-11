# Day 27 - real captured connectivity evidence

All commands run from the local machine (outside Azure, no VNet membership),
against the real deployed dev resources, on the dates/times shown by each
capture. Not constructed - every line below is copy-pasted terminal output.

## Before (public network access still Enabled)

```
$ nc -zv -w 6 sql-capstone-dev-2fqdji.database.windows.net 1433
Connection to sql-capstone-dev-2fqdji.database.windows.net port 1433 [tcp/ms-sql-s] succeeded!

$ nc -zv -w 6 sb-capstone-dev-tnjxju.servicebus.windows.net 443
Connection to sb-capstone-dev-tnjxju.servicebus.windows.net port 443 [tcp/https] succeeded!
```

## After (SQL publicNetworkAccess: Disabled; Service Bus unchanged - see
capstone/THREAT-MODEL.md and submission-day-27-task-1.md for why)

A raw TCP check alone is not decisive for SQL - Azure SQL's gateway is shared
infrastructure that accepts the TCP handshake regardless of any one server's
setting, then denies the connection one layer up (TDS pre-login):

```
$ nc -zv -w 8 sql-capstone-dev-2fqdji.database.windows.net 1433
Connection to sql-capstone-dev-2fqdji.database.windows.net port 1433 [tcp/ms-sql-s] succeeded!
```

So the decisive test is a real login attempt at the protocol layer, from a
throwaway Microsoft.Data.SqlClient console app (wrong credentials
deliberately - the point is what error comes back, not whether login
succeeds):

```
$ dotnet run -- "Server=sql-capstone-dev-2fqdji.database.windows.net;Database=sqldb-capstone-dev;User Id=nobody;Password=wrongpassword;Encrypt=True;Connection Timeout=15"
FAILED: SqlException: Reason: An instance-specific error occurred while establishing a connection to SQL Server. Connection was denied because Deny Public Network Access is set to Yes. For more information, see https://go.microsoft.com/fwlink/?linkid=2323206.
```

That is Azure's own explicit denial reason - not a timeout, not a generic
network error, but "Deny Public Network Access is set to Yes."

Service Bus, unchanged (see THREAT-MODEL.md, Information disclosure, for why
- Premium-only network restriction, not deployed):

```
$ nc -zv -w 8 sb-capstone-dev-tnjxju.servicebus.windows.net 443
Connection to sb-capstone-dev-tnjxju.servicebus.windows.net port 443 [tcp/https] succeeded!
```

## The app's own connection, from inside the VNet, still works

The deployed API's `GET /demo/db-ping` executes a real `SELECT 1` against the
same now-private SQL server, reached over the App Service VNet Integration ->
private endpoint path:

```
$ curl -H "Authorization: Bearer $TOKEN" https://capstone-dev-api-f7nsoj.azurewebsites.net/demo/db-ping
{"pinged":true,"result":1,"atUtc":"2026-09-11T09:28:47.5493003+00:00"}
```

Same server, same query, opposite outcome depending on whether the caller is
inside the VNet (the app, via its private endpoint route) or outside it (this
machine, over the public internet) - the private endpoint and VNet
integration are what make that difference real, not just declared in Bicep.
