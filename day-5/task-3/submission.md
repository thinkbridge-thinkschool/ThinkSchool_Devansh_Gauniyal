## GitHub link
https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-5/task-3/day-5/task-3

## Notes for mentor
Commands run, in order: `az provider register -n Microsoft.App --wait`, `az group create -n thinkschool-rg -l centralindia`, `az containerapp env create -n thinkschool-env -g thinkschool-rg -l centralindia`, `az containerapp env show -n thinkschool-env -g thinkschool-rg`. Real region used: Central India, confirmed as a supported location for both the subscription and the `Microsoft.App` resource type before creating anything. Both the resource group and the environment were genuinely created (`provisioningState: Succeeded` on both, confirmed independently via `az resource list -g thinkschool-rg`, which also confirmed the auto-created Log Analytics workspace `workspace-thinkschoolrgP59G` lives in the same resource group). Full redacted `env show` JSON and every command's real output is in `az-commands.md`; I scanned that file myself afterward and confirmed no raw subscription, tenant, client, or workspace GUID remains in it. No app was deployed -- task scope ends at the environment -- and the `az containerapp create` flags (`--ingress external`, `--target-port`, `--scale-rule*`) were documented in `notes.md`, verified against the actual installed CLI and official docs, not executed. One real finding: the task's `--scale-rule` isn't an actual flag name -- it's several separate `--scale-rule-*` flags plus `--min-replicas`/`--max-replicas`, documented accurately in `notes.md`. Commit hash: this same commit (a commit can't contain its own hash inside its own tree; see `git log -1` or the GitHub commit view). Identifiers (subscription ID, tenant ID, workspace customer ID, default domain, static IP, account email) were redacted deliberately, not omitted by error.

## What did you learn this session?
An empty Container Apps environment isn't actually free -- it silently provisions a metered Log Analytics workspace before any app is even deployed.

## What would break this?
A later deployment could fail if Task 2's arm64 image doesn't match whatever architecture this Consumption-plan environment actually runs container apps on.
