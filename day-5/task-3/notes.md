# Day 5 Task 3 — Notes

## What a resource group is, and why it's the unit of teardown

A resource group is just a labelled folder inside Azure that holds related resources together -- it has no cost or behavior of its own. It matters here because deleting a resource group deletes everything inside it in one action. That's why `thinkschool-rg` was created first: every resource this task creates goes inside it, so tearing the whole task down later is a single command instead of hunting down each piece individually.

## What a Container Apps environment is, and what "shared networking and logging boundary" means in practice

A Container Apps environment (`thinkschool-env`) is the shared platform that one or more container apps get deployed into. "Shared networking and logging boundary" means concretely: every app placed in this environment shares the same virtual network space and the same logging destination (verified live -- `az resource list -g thinkschool-rg` shows the environment shares its resource group with exactly one other resource: an auto-created Log Analytics workspace, which is where every app's logs in this environment would land). No app was deployed in this task, so right now the environment has nothing running inside it -- it's an empty, ready platform.

## App vs revision

An "app" in Container Apps is the named deployment target -- the thing you `az containerapp create` once and then update repeatedly. Each time you deploy a new version, Container Apps creates a "revision" -- an immutable snapshot of that app's configuration and container image at that point in time. Because old revisions aren't deleted or overwritten when a new one is created, multiple revisions can run side by side: traffic can be split between an old and new revision (canary), or fully cut over only once the new one is verified healthy (blue-green). Immutability is what makes this safe -- a revision you're not actively sending traffic to still exists exactly as it was, ready to take traffic back instantly if the new one misbehaves.

## The `az containerapp create` flags named in the task -- documented, NOT run

No app was deployed in this task; the task's own scope stops at the environment. These flags were verified against the actual installed CLI (`az containerapp create --help`, CLI version 2.89.0) and cross-checked against the official docs: https://learn.microsoft.com/en-us/cli/azure/containerapp?view=azure-cli-latest#az-containerapp-create

- **`--ingress external`** -- controls whether the app gets a publicly reachable URL (`external`) or is only reachable from other apps inside the same environment (`internal`). Not used here, since nothing was deployed.
- **`--target-port`** -- tells Container Apps which port inside your container actually receives HTTP traffic, so it knows where to route an incoming request. This is the container-side equivalent of the `-p 8080:8080` port mapping from Task 2's local `docker run`.
- **`--scale-rule`** -- **this exact flag name does not exist.** The task text simplifies what is actually several separate flags: `--scale-rule-name`, `--scale-rule-type` (defaults to `http`), `--scale-rule-metadata`, and `--scale-rule-auth`, alongside the separate `--min-replicas`/`--max-replicas` flags that set the floor and ceiling Container Apps scales between. Together these define what triggers scaling (e.g. concurrent HTTP requests, a queue depth) and how far it's allowed to scale.

## What Log Analytics is doing here, and why it's the part that consumes credit

`az containerapp env create` was not given an existing logging workspace, so it silently created one for you: `workspace-thinkschoolrgP59G` (confirmed via `az resource list -g thinkschool-rg`, type `Microsoft.OperationalInsights/workspaces`). Every log line any app in this environment ever produces -- and even system-level logs from the environment itself, with zero apps deployed -- gets ingested into this workspace. Log Analytics has a free monthly data-ingestion allowance, then bills per GB beyond it. The resource group and the environment itself have no such per-GB meter, which is why this workspace is the one thing in this task actually capable of consuming your student credit over time. Current figures: https://azure.microsoft.com/en-us/pricing/details/monitor/

## How this connects to Task 2

Task 2 produced a real, working container image (`quotes-api:0.1.0`) sitting locally in this Mac's Docker image store -- nothing left this machine. A Container Apps environment like `thinkschool-env` is the kind of place that image would eventually get deployed to, once pushed to a registry Azure can pull from (Container Apps can't pull directly from a local Docker daemon). **No deployment happened in this task.** The environment exists, empty, as the platform a future deployment would target.

## Teardown

```
az group delete -n thinkschool-rg --yes
```
This deletes the resource group, the Container Apps environment, and the auto-created Log Analytics workspace together, since `az resource list` confirmed both live inside `thinkschool-rg`. It is not reversible. Until this is run, both resources continue to exist and the Log Analytics workspace continues to be capable of accruing ingestion charges against your credit, even with no app deployed.
