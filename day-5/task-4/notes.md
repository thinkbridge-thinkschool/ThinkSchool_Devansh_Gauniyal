# Day 5 Task 4 — Notes

## What `azd` actually does, and which manual steps it replaces

`azd` (Azure Developer CLI) wraps three stages behind one command: packaging (building the app -- here, `dotnet publish /t:PublishContainer`, the same no-Dockerfile mechanism from Task 2), provisioning (creating the Azure resources via generated Bicep), and deployment (pushing the image and updating the running app). It replaces what Tasks 2 and 3 did by hand across two separate exercises: manually running `dotnet publish` to build a container image, and manually running `az group create` / `az containerapp env create` to stand up infrastructure. `azd up` does both in one command, plus the actual container registry push and app deployment that neither Task 2 nor Task 3 did.

## What Bicep is, and what "infrastructure as code" means here

Bicep is a declarative language for describing Azure resources as a file instead of a sequence of `az` commands. "Infrastructure as code" in practice, for this task, meant: `azd init` generated `azure.yaml` (which app maps to which Azure resource type) and, once materialized with `azd infra gen`, a set of `.bicep` files describing exactly what to create. Running `azd up` reads those files and calls Azure's Resource Manager to create everything they describe, in dependency order, instead of me typing each `az` command by hand as in Task 3.

## The real generated Bicep, explained field by field

`main.bicep` creates a resource group named `rg-<environmentName>` and delegates everything else to `resources.bicep`. `resources.bicep` provisions:
- A **Log Analytics workspace** and **Application Insights** (plus a portal dashboard) -- logging and telemetry for the app.
- A **Container Registry** -- where the built image gets pushed.
- A **Container Apps environment** -- the same kind of resource Task 3 built by hand.
- A **user-assigned managed identity** -- lets the Container App pull from the registry without embedding credentials.
- The **Container App** itself (`quotes-api`), with `ingressTargetPort: 8080`, `scaleMinReplicas: 1`/`scaleMaxReplicas: 10`, and a `fetch-container-image.bicep` module that reuses the currently-deployed image on redeploys instead of resetting it.

## How this builds on Task 2 and Task 3 -- and what actually happened

Task 2 produced a container image that only ever existed locally, in this Mac's Docker image store. Task 3 built an empty Container Apps environment (`thinkschool-env`) with nothing deployed into it. The plan going in was for `azd` to reuse that environment. **What actually happened: it did not.** `azd`'s generated Bicep always provisions its own fresh resource group and environment -- confirmed directly by reading `main.bicep` before ever running `azd up`. In practice this collided with a real subscription limit: this "Azure for Students" subscription allows exactly **one** Container Apps environment, globally, for the whole subscription (not per region -- confirmed by hitting two different errors, a regional cap and then a global cap, when trying two different regions). Task 3's `thinkschool-env` was occupying that single slot, so nothing else could be created anywhere until it was removed. With explicit approval, `thinkschool-rg` (containing `thinkschool-env`) was deleted, freeing the slot for `azd`'s own environment, `cae-hvnke3dqrbwrq`, in Central India. Task 3's git deliverables (already committed and pushed beforehand) were unaffected by deleting the live resource.

## The architecture issue -- what actually went wrong and how it was found

Before ever running `azd up`, I confirmed from `azd`'s own source code that it hardcodes the `linux-x64` runtime identifier for .NET container builds, regardless of host machine architecture -- so the arm64-vs-amd64 CPU architecture concern from the task text was already handled correctly by `azd` itself, and the pushed image genuinely was `amd64` (confirmed via the registry's own manifest metadata). That part worked exactly as expected on the first try.

What actually broke was a narrower, related problem: `linux-x64` is the **glibc**-targeted identifier, and the copied project's `<ContainerBaseImage>` was still set to the Alpine (musl-based) tag from Task 2. The container deployed, was reachable, pulled its image -- and then crashed on startup with `DllNotFoundException` / `Error relocating ...: symbol not found`, because the glibc-built native SQLite library can't load on a musl base image. This only became visible by actually curling the live URL and getting a `504`, then checking real container logs in Log Analytics -- the deployment itself reported "SUCCESS" the whole time. The fix was switching `<ContainerBaseImage>` to the default, glibc-based `mcr.microsoft.com/dotnet/aspnet:10.0` tag, since `azd`'s RID can't be changed to match Alpine.

## What a revision is, and how `azd up` produces one

A revision is an immutable snapshot of a Container App's configuration and image at one point in time. Every time `azd up` or `azd deploy` runs, it creates a new revision pointing at the freshly pushed image and shifts traffic to it -- visible directly in this deployment's `az containerapp revision list` output, which showed an initial revision (`quotes-api--0000001`, 0% traffic, unhealthy -- from the very first provisioning pass before any real image existed) and a second one (`quotes-api--azd-...`, 100% traffic) once the actual app was pushed and deployed.

## Complete teardown commands

```
# Removes everything azd created in this task (rg-task4 and everything inside it):
azd down --force --purge

# Removes Task 3's original resources (already deleted during this task, kept here for the record):
az group delete -n thinkschool-rg --yes
```
Both are irreversible resource-group deletions. Resources left running continue to consume subscription credit -- the Container Registry in particular carries a base cost regardless of usage (see Task 4's billing table for pricing page links; no prices are invented here).
