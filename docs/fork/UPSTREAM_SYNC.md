# Controlled upstream synchronization

This document belongs to the pAthena3 engine development history, never to the clean upstream mirror main. It is a procedure and proposal; none of its branch, push, merge or sync steps has been executed by this documentation change. See [UPSTREAM_CHANGES.md](UPSTREAM_CHANGES.md) for exact patch provenance and qualification.

## Branch ownership

| Branch | Ownership and allowed content |
| --- | --- |
| main | Clean mirror of aethai/ro3emu-server main, no pAthena3 patches or this fork documentation. |
| develop | Only approved pAthena3 engine changes and maintained fork documentation on a selected upstream baseline. |
| feature/*, poc/* | Isolated reviewed development; no direct production implication. |
| upstream-sync/* | Explicit, reviewed integration of a selected clean main SHA into develop. |

pAthena3-deploy consumes an exact tested commit SHA reachable from approved engine develop. A branch name is provenance, never a floating dependency. Neither a newer develop HEAD nor a clean upstream mirror automatically changes the deploy pin. Test and approve each pin; promote applicable dependency locks together. A documentation-only descendant may reuse executable evidence only with an exact diff proving that code, tests and build/dependency inputs are unchanged; record both SHAs and this qualification explicitly.

## Observed state and transition proposal

On 7 November 2026, local/fork/upstream main all resolve to `4559db162df0ef0739ca58690e5a64164ccf4fb9`. The fork is not ahead or behind upstream. Local and remote develop do not exist. At the initial hardening checkpoint the PoC had eight local engine commits on baseline `b1504562913fd5716a3168e99ac89713cd66254d`, followed by its documentation-only commit. The mandatory B1/cleanup follow-up now adds 370edac and db28471; the complete executable series contains ten commits, plus maintained fork documentation. They are unpushed. Main has 228 commits not in the PoC; the current PoC has ten executable commits not in main. No current claim of develop reachability is possible.

Recommended initial adoption, requiring explicit approval: establish develop at the reviewed PoC documentation tip after verifying the frozen code checkpoint and documented evidence. This preserves the tested upstream baseline, complete original patch SHAs and all parent relationships. It avoids mixing a 228-commit upstream integration into a safety acceptance. Main stays untouched. Then review the first upstream-sync to the selected clean main SHA separately. Deliberately having develop on an older recorded upstream baseline is allowed; it must not be represented as current with upstream.

If policy instead requires initial develop to start at current main, prepare a later feature integration from that SHA and merge the complete existing PoC history there, resolve conflicts by invariant, and repeat the full acceptance matrix before promotion. Do not cherry-pick only the headline PoC commit or drop dependent safety commits. This alternative is a new integration exercise, not already qualified by the current tests. Preserve the original SHAs in the ledger even if an approved workflow eventually requires cherry-picks and records replacement SHAs.

Before an approved push/PR:

1. Review the complete ten-commit executable engine series and all maintained fork-documentation commits; agree the initial develop baseline and governance text. Current local gitlink is an isolated PoC exception, not yet compliance with the develop-source rule.
2. Recheck local/remote main, develop, current worktree/branch, full SHAs, clean status and absence of other active work. If develop appeared or advanced, stop and reconcile the proposal; never reset or overwrite it.
3. Approve publishing the existing PoC history and establishing/promoting develop. Branch creation, pushes, PR creation and merge each remain subject to owner authorization; this document grants none.
4. Preserve commit ancestry with a reviewed promotion (no squash if retaining tested SHAs is the chosen provenance). Record the approved develop tip and the exact engine commit selected for deploy.
5. After approved engine publication, prove the selected object is remotely reachable from develop and reproducibly available to a clean selective checkout. Never open a deploy PR with an unreachable local engine SHA.
6. Review the deploy gitlink and locks together, run exact-pin CI, including the real shallow-checkout regression, and record which SHA was actually executed. If integration changes executable bytes, all affected tests must run again.
7. Keep publication/pin promotion separate from release. No game-data compatibility promotion, Kubernetes changes or database operation is implied.

## Future upstream-sync procedure

1. Schedule an explicit sync after checking no uncommitted work or conflicting active task. Inventory local/remote refs and record the approved develop tip, selected main SHA, previous upstream baseline, current deploy pin and lockfile hashes. Read upstream release/security/schema changes. Freeze exact target SHAs.
2. Under separate approval, update mirror main only by a verified fast-forward to the chosen upstream commit. Refuse divergence; investigate rather than force-reset. Never merge develop into main and never put fork documentation there.
3. Under separate approval, create an upstream-sync branch from the frozen develop tip. Merge the selected main SHA into that integration branch. Never merge a moving reference blindly. Preserve original patch ancestry and review the resulting combined diff.
4. Review every file in UPSTREAM_CHANGES.md against upstream changes, especially AccountStore startup, DbWriter/DbReadPool, native authorizer, execution forwarding, model/schema ownership and audit metadata. Resolve conflicts to preserve invariants, not by blanket ours/theirs. Record equivalent upstream implementations and every new conflict-resolution patch.
5. Prove unchanged persisted plan fields/types/fingerprints, receipts, revision CAS, commit-before-reply, grouped savepoint behavior, native read/thread/item/economic guards and transaction ownership. Compare schema ownership and opening policy before bootstrap; verify no data-mutating startup migration. Do not migrate a new gameplay domain during a sync.
6. Run the qualification commands in UPSTREAM_CHANGES.md, appropriate changed-upstream tests and the complete synthetic engine/extension suite with locked dependencies. Test SQLite write/restart, migration adoption/refusal/private permissions and adversarial EF escapes. Report SQL Server kernel evidence separately. Compare old passed tests by name; do not count absent or newly skipped tests as preserved passes. Reassess any known failure instead of assuming the old exception applies to a new upstream release.
7. Review changed NuGet graph and lockfiles, EF/provider API compatibility, shallow exact-pin behavior and artifact reproducibility. Measure queue/model/commit latency if persistence behavior changes. No live database, private export or Kubernetes is needed for this gate.
8. Update UPSTREAM_CHANGES.md with new upstream baseline, full integration/patch SHAs, file deltas, resolved conflicts, retired equivalent patches and exact test evidence. Keep operational logs and personal/private data out of Git.
9. Present the frozen integration for independent review. Only after approval may it be pushed, offered as a PR to develop and merged. Main remains a clean mirror. Record final ancestry and tested commit; do not automatically move the deploy pin.
10. In a separate reviewed deploy change, select the exact approved develop commit, validate its remote availability, locks and synthetic build/tests, and update only the gitlink and related reproducibility inputs. Deployment/release and operational migration require separate authorization.

## Governance and enforcement

Zero-Touch-First remains the default: deploy-specific provider setup/migrations/operations live in deploy. Engine edits require explicit approval, a small generic necessity, alternatives, a patch ledger and safety evidence. No pAthena3-specific gameplay logic in engine integration points; no third submodule, auto-updating submodule, duplicated business logic or automatic upstream sync.

Suggested later deploy AGENTS.md boundary wording (proposal only, not applied):

> Consume an exact tested SHA reachable from approved pAthena3-server/develop, never a floating ref. Ordinary deployment work makes no engine edits. Explicitly approved generic engine hooks are developed on isolated feature/poc branches and documented in docs/fork with exact provenance, alternatives and regression evidence. Main is a clean upstream mirror; upstream-sync branches integrate chosen upstream SHAs into develop only after review. Promote engine gitlink and dependency locks reproducibly. No automatic sync, pin promotion, push, merge or deployment.

Current CI validates the exact committed pin and clean checkout; it does not yet prove remote develop reachability. A later deploy-side provenance check may use an explicitly selected develop tip and complete pre-fetched history, fail closed when proof is absent, and never fetch/repair/float automatically. Do not weaken the ordinary shallow exact-pin check to add an ancestry assumption that shallow CI cannot prove. Design and test that separate promotion gate before claiming it is enforced.

## B1/B2 publication gate

B1 is a required safety fix, not an optional future domain: include 370edac5c02f09e7a21e8fa178951fdef7819877 and db28471286b389b3fc3b659d99966aef3cbaf40d and the associated documented regressions before approving develop. The latest exact tested tip must include both. Repeat all qualification against that committed gitlink; the earlier e7dbcbf evidence alone is insufficient. No upstream sync is authorized; retain the b150456 baseline for this initial promotion.

Read-only publication check on 7 November 2026: engine fork main remains 4559db162df0ef0739ca58690e5a64164ccf4fb9 and is identical to aethai upstream main (zero ahead/behind). Develop remains absent. GitHub commit lookups for e7dbcbf and 4f0c5f4 return HTTP 422, no such commit in the remote fork. Therefore an exact local gitlink is not yet a remotely reproducible dependency; B2 stays open until approved publication, develop reachability proof and a clean remote qualification succeed. No main/develop refs were changed.

Deploy remote main was separately frozen at bfb19aa456db40d021ac4811c4bb851dfb54107f, not the stale local main or origin/main. Read-only three-way text simulations against common ancestor 787df6624c1327aa3d1abb990766e1d80d696c8b confirm one conflict region in src/Directory.Packages.props and six in tests/Custom.Host.Tests/packages.lock.json. These are two conflicting paths, not two lockfiles. All eight central package versions common to PoC and main agree, including EF/SQLite/Pkcs 10.0.14. The shared test graph differs because main includes Identity packages while the PoC introduces the engine's EF transitive dependency.

Do not resolve this with ours/theirs lockfile copying or duplicate PackageVersion entries. During a later approved deploy integration, preserve the independent Identity configuration, deduplicate the common central package declarations, regenerate the combined affected dependency graph, retain the explicit Pkcs security floor, and require locked restore for both extension and persistence solutions. The present isolated PoC does not alter Identity or simulate that combined runtime. Its own lockfiles have been re-evaluated and locked-restored without byte changes. The combined-main qualification remains a publication prerequisite.

Before deploy-main consumes the pin, approve the proposed AGENTS.md exception for minimal generic engine hooks and the clean-main/develop exact-SHA provenance policy, establish approved develop through the separately authorized path, verify remote availability and selected-commit ancestry, resolve/requalify the combined deploy dependency configuration and obtain review. Current CI only proves exact gitlink and checkout cleanliness. A separate provenance/promotion proof must not assume historical ancestry is available in shallow ordinary CI. No automatic branch creation, sync, merge, rebase, push, PR or deployment is implied.
