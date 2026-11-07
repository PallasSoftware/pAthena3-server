# pAthena3 engine changes relative to upstream

This is maintained fork documentation on `poc/persistence-provider-abstraction`, intended for an approved `develop`. It must not be merged into the clean upstream mirror `main`. It describes supported boundaries, not an operational rollout. See [UPSTREAM_SYNC.md](UPSTREAM_SYNC.md) for branch ownership and promotion gates.

## Fixed provenance

- Upstream: `https://github.com/aethai/ro3emu-server`.
- Fork: `https://github.com/PallasSoftware/pAthena3-server`.
- Original tested upstream baseline: `b1504562913fd5716a3168e99ac89713cd66254d`.
- First EF checkpoint: `e5215176a23c38ca13d6c7238abe4780014d4dc8`.
- Remediation checkpoint: `36cad1e1dd4084b69535ef946e0fd67989af8c52`.
- Last executable hardening checkpoint: `4f0c5f4ea70b5383ca8e609e0277fd58aa17dd33`. The subsequent fork-documentation commit changes Markdown only.
- Deploy code checkpoint: `34fde7da65be32b3fce78dda54e7105f01ecd956`, branch/worktree `poc/persistence-provider-abstraction`, `/Users/marco/Projects/pAthena3-deploy-persistence-poc`.

Remote observations on 7 November 2026: fork `main` and upstream `main` both resolve to `4559db162df0ef0739ca58690e5a64164ccf4fb9`; GitHub's exact-commit comparison reports identical, zero ahead/behind. Local engine `main` and `origin/main` agree. Neither local nor remote `develop` exists. `git ls-remote --heads origin main develop` completed successfully and returned only main. No fetch, branch/ref update or sync was performed. Observations must be refreshed before future promotion.

The PoC is based on the older baseline: the merge base of local main and the hardening checkpoint is `b150456…`; `git rev-list --left-right --count main...4f0c5f4` is `228 8`. Those 228 upstream-only commits have not been integrated or qualified with this PoC. Main being a clean mirror does not mean this PoC is current with main.

## Scope and invariants

EF Core 10 is used for the bounded realm-state route only. Existing writer/reader coordination remains authoritative for savepoints, revision CAS, receipts, grouped commit, recovery and commit-before-reply. No persisted plan field/fingerprint, economic rule, account identifier or Identity responsibility is changed. Existing unmapped tables keep their legacy owner.

The current GameDbContext model, execution seam and SQLite native enforcement are engine-side. Deploy owns provider-specific migration projects, schema compatibility policy, copy-only adoption, transfer preflight, composition, CLI and CI. No production/staging data was opened. SQL Server evidence is a standalone transaction kernel; the engine writer/reader still require SQLite. No SQL Server game-runtime support is implied.

## Patch ledger

Each immutable commit below records its exact parent delta, file paths and required regression classes. Counts include tests and overlap across commits; do not sum them to count distinct files. Paths are relative to this engine repository. The ledger is generated from `git show --numstat` and must be updated when a patch is added, superseded or accepted upstream. A future conflict resolution gets its own entry and requalification; do not silently rewrite the provenance.

### e5215176a23c38ca13d6c7238abe4780014d4dc8

Prototype EF Core realm persistence in guarded transactions

Introduce the bounded EF model, factory/enlistment entry points and season-state vertical route. Preserve native writer ownership and plan identities; this is the initial experiment, superseded by the following safety fixes.

Required regressions: EfPersistenceTests, ReceiptFieldNamesTests, existing season-state and DbWriterFaultTests; deploy Code First/migration/adoption tests for both providers. This commit alone is not safe to promote without its dependent fixes.

| File | Added | Removed |
| --- | ---: | ---: |
| `Directory.Packages.props` | 1 | 0 |
| `src/Ro3Emu.Server/Accounts/AccountStore.cs` | 4 | 3 |
| `src/Ro3Emu.Server/Accounts/Persistence/DbPlan.cs` | 5 | 0 |
| `src/Ro3Emu.Server/Accounts/Persistence/DbReadPool.cs` | 5 | 2 |
| `src/Ro3Emu.Server/Accounts/Persistence/DbWriter.cs` | 5 | 2 |
| `src/Ro3Emu.Server/Accounts/Persistence/GameDbContext.cs` | 96 | 0 |
| `src/Ro3Emu.Server/Accounts/Persistence/RoleItemPlans.cs` | 6 | 3 |
| `src/Ro3Emu.Server/Accounts/Persistence/SeasonStatePlans.cs` | 15 | 10 |
| `src/Ro3Emu.Server/Ro3Emu.Server.csproj` | 1 | 0 |
| `src/Ro3Emu.Server/ServerHost.cs` | 6 | 1 |
| `tests/Ro3Emu.Tests/EfPersistenceTests.cs` | 217 | 0 |

### a9e31bd081be70f51f5635fae0b2cbc396007a87

Share guarded EF execution across writer jobs and economy children

One lazy registered GameDbContext per worker job and shared execution through all six economy wrappers and RoleItem children. Remove runtime fallback, protect tracked coordination writes, and disable EF auto-savepoints. Wrapper edits forward context only; their business rules and captured fields stay unchanged.

Required regressions: EfPersistenceTests: nested envelope uses the same factory/context, refusing factory has no fallback, rollback and commit-before-reply, revision/receipt mutation refusal, auto-savepoint refusal; ReceiptFieldNamesTests and DbWriterFaultTests.

| File | Added | Removed |
| --- | ---: | ---: |
| `src/Ro3Emu.Server/Accounts/AccountStore.cs` | 1 | 0 |
| `src/Ro3Emu.Server/Accounts/Persistence/DbPlan.cs` | 6 | 7 |
| `src/Ro3Emu.Server/Accounts/Persistence/DbPlanExecution.cs` | 38 | 0 |
| `src/Ro3Emu.Server/Accounts/Persistence/DbReadPool.cs` | 2 | 1 |
| `src/Ro3Emu.Server/Accounts/Persistence/DbWriteGuard.cs` | 19 | 0 |
| `src/Ro3Emu.Server/Accounts/Persistence/DbWriter.cs` | 2 | 1 |
| `src/Ro3Emu.Server/Accounts/Persistence/EconomyEnvelopePlan.cs` | 4 | 2 |
| `src/Ro3Emu.Server/Accounts/Persistence/EconomyTransactionPlan.cs` | 4 | 2 |
| `src/Ro3Emu.Server/Accounts/Persistence/EfCoordinationGuard.cs` | 40 | 0 |
| `src/Ro3Emu.Server/Accounts/Persistence/EscrowEconomyTransactionPlan.cs` | 4 | 2 |
| `src/Ro3Emu.Server/Accounts/Persistence/EventEconomyTransactionPlan.cs` | 4 | 2 |
| `src/Ro3Emu.Server/Accounts/Persistence/FamilyEconomyTransactionPlan.cs` | 4 | 2 |
| `src/Ro3Emu.Server/Accounts/Persistence/GameDbContext.cs` | 29 | 7 |
| `src/Ro3Emu.Server/Accounts/Persistence/ProgressionEconomyTransactionPlan.cs` | 4 | 2 |
| `src/Ro3Emu.Server/Accounts/Persistence/RoleItemPlans.cs` | 4 | 6 |
| `src/Ro3Emu.Server/Accounts/Persistence/SeasonStatePlans.cs` | 4 | 19 |
| `tests/Ro3Emu.Tests/EfPersistenceTests.cs` | 118 | 7 |

### a3480de04142eaf11f2dfe1acd7bc3d7659fe457

Validate external schema ownership before realm bootstrap

Add generic read-only IRealmSchemaPolicy before bootstrap; skip only explicitly externally owned complete table DDL. Classify operational migration metadata in the authoritative audit. Actual migration IDs and opening/adoption policy remain in deploy.

Required regressions: EfPersistenceTests and deploy migration/adoption tests: unmanaged legacy, fresh Code First, adopted copy, missing/unknown/newer history, unchanged realm on refusal, metadata audit, baseline rollback-to-zero refusal and private file permissions.

| File | Added | Removed |
| --- | ---: | ---: |
| `src/Ro3Emu.Server/Accounts/AccountStore.cs` | 17 | 3 |
| `src/Ro3Emu.Server/Accounts/Persistence/CoreSchemaPlans.cs` | 10 | 4 |
| `src/Ro3Emu.Server/Accounts/Persistence/DbPlanExecution.cs` | 2 | 1 |
| `src/Ro3Emu.Server/Accounts/Persistence/DbWriter.cs` | 4 | 2 |
| `src/Ro3Emu.Server/Accounts/Persistence/GameDbContext.cs` | 4 | 0 |
| `src/Ro3Emu.Server/Accounts/Persistence/RealmSchemaOwnership.cs` | 28 | 0 |
| `src/Ro3Emu.Server/Accounts/Persistence/SeasonStatePlans.cs` | 10 | 4 |
| `src/Ro3Emu.Server/Economy/EconomySnapshotReader.Schema.cs` | 4 | 0 |

### 36cad1e1dd4084b69535ef946e0fd67989af8c52

Enforce declared EF realm writes at each child boundary

Enforce current-child declared realm keys on tracked writes and command execution, restore authority at child boundaries, and refuse EF raw/bulk bypasses and foreign-thread writes through the native guard.

Required regressions: EfPersistenceTests: undeclared keys, save callbacks, bulk/raw writes, nested child permissions, cross-thread refusal, rollback and preserved receipts; ReceiptFieldNamesTests and full synthetic engine suite.

| File | Added | Removed |
| --- | ---: | ---: |
| `src/Ro3Emu.Server/Accounts/Persistence/DbPlanExecution.cs` | 26 | 4 |
| `src/Ro3Emu.Server/Accounts/Persistence/DbWriteGuard.cs` | 10 | 1 |
| `src/Ro3Emu.Server/Accounts/Persistence/EconomyEnvelopePlan.cs` | 1 | 1 |
| `src/Ro3Emu.Server/Accounts/Persistence/EconomyTransactionPlan.cs` | 1 | 1 |
| `src/Ro3Emu.Server/Accounts/Persistence/EfCoordinationGuard.cs` | 9 | 3 |
| `src/Ro3Emu.Server/Accounts/Persistence/EscrowEconomyTransactionPlan.cs` | 1 | 1 |
| `src/Ro3Emu.Server/Accounts/Persistence/EventEconomyTransactionPlan.cs` | 1 | 1 |
| `src/Ro3Emu.Server/Accounts/Persistence/FamilyEconomyTransactionPlan.cs` | 1 | 1 |
| `src/Ro3Emu.Server/Accounts/Persistence/GameDbContext.cs` | 6 | 1 |
| `src/Ro3Emu.Server/Accounts/Persistence/ProgressionEconomyTransactionPlan.cs` | 1 | 1 |
| `src/Ro3Emu.Server/Accounts/Persistence/RoleItemPlans.cs` | 2 | 2 |
| `tests/Ro3Emu.Tests/EfPersistenceTests.cs` | 40 | 1 |

### d615c26cae7fe1be3ecddf3dd98e809b1375beaa

Enforce worker-owned transactions across EF and native escape paths

Add standard EF transaction interception plus native plan-execution authority. Reject EF/direct/raw transaction control. Recover orphaned native transactions after SqliteTransaction wrapper invalidation, aborting the whole group rather than returning false success. Only the worker can perform cleanup.

Required regressions: EfTransactionOwnershipTests: sync/async EF begin/commit/rollback/savepoints, raw SQL, direct native transaction-object escapes, caught exceptions, failed-group earlier data/revision/receipt rollback, subsequent writer/reader health; DbWriterFaultTests and ReceiptFieldNamesTests.

| File | Added | Removed |
| --- | ---: | ---: |
| `src/Ro3Emu.Server/Accounts/Persistence/DbPlanExecution.cs` | 6 | 1 |
| `src/Ro3Emu.Server/Accounts/Persistence/DbReadPool.cs` | 1 | 0 |
| `src/Ro3Emu.Server/Accounts/Persistence/DbWriteGuard.cs` | 42 | 0 |
| `src/Ro3Emu.Server/Accounts/Persistence/DbWriter.cs` | 5 | 1 |
| `src/Ro3Emu.Server/Accounts/Persistence/EfTransactionGuard.cs` | 46 | 0 |
| `src/Ro3Emu.Server/Accounts/Persistence/GameDbContext.cs` | 1 | 1 |
| `tests/Ro3Emu.Tests/EfTransactionOwnershipTests.cs` | 202 | 0 |

### a44ebe171f2c3f97d1d44167a3c0ea5a4d123918

Deny EF runtime DDL and schema-changing pragmas

Deny the entire EF runtime DDL category, attach/detach and mutating pragmas in the existing native authorizer. Legacy bootstrap and unregistered offline migration contexts retain their distinct authority.

Required regressions: EfRuntimeDdlTests: ALTER, CREATE/DROP table/index/trigger and additional schema operations, unchanged schema/data/revision, absent receipt and healthy next write. Deploy standalone tests prove offline DDL and transaction control remain allowed.

| File | Added | Removed |
| --- | ---: | ---: |
| `src/Ro3Emu.Server/Accounts/Persistence/DbWriteGuard.cs` | 9 | 0 |
| `tests/Ro3Emu.Tests/EfRuntimeDdlTests.cs` | 90 | 0 |

### 1f5854dac81cdb7c56654b57fb9aca0a57edcea5

Verify every concrete persistence plan implements an executor

Require every concrete DbPlan/DbReadPlan in the engine assembly to implement either supported execution route; reject a deliberately incomplete fixture. This is a CI contract, not restored C# compile-time enforcement.

Required regressions: Both DbPlanContractTests plus ReceiptFieldNamesTests. Extend the assembly scan if future plan assemblies are added.

| File | Added | Removed |
| --- | ---: | ---: |
| `tests/Ro3Emu.Tests/DbPlanContractTests.cs` | 38 | 0 |

### 4f0c5f4ea70b5383ca8e609e0277fd58aa17dd33

Refuse file-based database lifecycle escapes on borrowed EF contexts

Guard EnsureCreated/EnsureDeleted sync and async on borrowed contexts because SQLite file deletion can bypass SQL interception. A small public DatabaseFacade override avoids an EF internal-service fork.

Required regressions: EfTransactionOwnershipTests for borrowed lifecycle refusal and continued health; deploy StandaloneTransactionTests for positive standalone create/delete sync and async. Re-run these on EF/provider upgrades.

| File | Added | Removed |
| --- | ---: | ---: |
| `src/Ro3Emu.Server/Accounts/Persistence/GameDbContext.cs` | 6 | 0 |
| `src/Ro3Emu.Server/Accounts/Persistence/WorkerDatabaseFacade.cs` | 21 | 0 |
| `tests/Ro3Emu.Tests/EfTransactionOwnershipTests.cs` | 6 | 1 |

## Aggregate merge burden and alternatives

Executable hardening versus `36cad1e`: 10 distinct files, +472/-3. Only three original upstream paths are edited again: DbWriteGuard (+51/-0), DbWriter (+5/-1), DbReadPool (+1/-0). They were already among the prior 18; no new original upstream path is touched. DbPlanExecution and GameDbContext were already new PoC files. Five new files contribute 402 lines (335 tests, 67 production adapters). The six economic wrappers and RoleItemPlans are unchanged during hardening.

Total executable fork versus `b150456`: 28 distinct files, +1231/-49. This comprises 18 existing upstream files (+189/-49) and 10 new files (+1042/-0). The two documentation files in this directory are additional new Markdown paths and are excluded from those executable counts.

Keep all 18 existing-file changes for the present bounded route. Externally wrapping AccountStore or copying stores cannot own internal job savepoints/context lifetime, and would duplicate business logic. The six wrapper forwards plus RoleItem forwarding are small stable hooks. An ambient/static/AsyncLocal context would reduce explicit call sites but weaken instance isolation and nested authority visibility; a complete plan-hierarchy rewrite creates greater merge cost. Do not implement either merely to reduce line counts.

The smallest future upstream candidates are independent generic schema-policy and worker-execution/child-propagation hooks with their tests. Native authority and transaction cleanup must remain with the worker; an external DbCommandInterceptor cannot intercept direct transaction-object operations or recover worker state. The bounded EF model is a retained fork decision, not automatically upstream-ready: externalizing it requires a reviewed model/context contract, whose cost currently exceeds the small model. No deployment-specific logic belongs in upstream.

## Mandatory qualification and limits

From the deploy checkout, reuse PowerShell entrypoints:

```powershell
pwsh ./ci/persistence/tests/Test-PersistencePoc.ps1
pwsh ./ci/persistence/Test-PersistencePoc.ps1 -SqlServer
pwsh ./ci/persistence/Test-PersistencePoc.ps1 -Regression
pwsh ./ci/extensions/Test-Extensions.ps1
pwsh ./ci/tests/Test-ProjectStandard.ps1
pwsh ./ci/Test-PowerShell.ps1
pwsh ./ci/workflow/Test-CicdWorkflow.ps1
```

The selected engine must be clean, HEAD equal to the committed deploy gitlink, with locked restore. Baseline qualification additionally requires complete pre-existing history and the fixed baseline to be an ancestor; never repair or update a submodule automatically. Use invented fixtures only. The optional SQL Server command creates/removes only its own isolated synthetic container, no host volume. SQL Server result counts must be reported separately from engine SQLite runtime results.

Executed at the hardening code checkpoint: 121 focused engine tests passed; 40 deploy PoC tests passed, including 7 actual SQL Server kernel checks. Full engine: 1380 passed, 1338 skipped, one known osx-arm64 package failure in PanelUpdateTests. Of the 1267 original-baseline, 1276 initial-EF and 1289 remediation previously passing tests, zero regressed by exact test-name comparison. The deploy report `docs/persistence-ef-hardening.md` holds the final extension/static evidence and artifact paths.

These are bounded synthetic guarantees. Windows ACL behavior, pooled contexts, load p95/p99, complete gameplay coverage and SQL Server runtime fencing/write authority/deadlock/uncertain-commit recovery remain unqualified. Native Rollback/Dispose wrapper invalidation forces whole-group failure and cleanup; it cannot be caught and treated as a successful job. Arbitrary trusted .NET code, reflection, direct filesystem writes or replacement of the native authorizer are not sandboxed.

For every future patch: record full SHA/parent, exact changed paths, reason it cannot safely remain in deploy, upstream alternative/status, plan/schema/receipt compatibility, per-provider regressions and unresolved gates here. When upstream accepts an equivalent patch, record the upstream SHA and prove its equivalence before retiring the fork change in a reviewed upstream-sync. Never remove a safety hook just because a conflict is difficult.
