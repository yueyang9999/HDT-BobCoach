# Historical: BobCoach v1.1.0 Release SDD Progress

- Task 1: in progress
- Task 2: pending
- Task 3: pending
- Task 4: pending
- Task 5: pending
- Final GitHub Release: pending final approval required by `AGENTS.md`

- Task 2-3: `release: complete BobCoach 1.1.0 package contract`; focused docs, AppData, package-builder, and lifecycle checks passed. Status-strip images: controls 1600x817/255361 bytes, result 480x220/16729 bytes, settings-entry 1600x142/67767 bytes; all progressive JPEG. Residual concern: Windows 11 24H2 final physical smoke remains incomplete, and GitHub publication still requires explicit owner approval.

# Active: 2026-07-31 Next-Season Algorithm Optimization

Plan: `docs/superpowers/plans/2026-07-31-next-season-algorithm-optimization.md`
Branch start: `7cd9786`

Data boundary approved 2026-07-31: season-independent engineering may proceed, but all uncalibrated policy work remains Shadow. Effect acceptance is `blocked-by-season-data` until at least 300 trustworthy, version-matched labeled scenarios, Shadow comparison, and human review are available. Synthetic and prior-season data prove engineering behavior only and must not support improvement claims.

- Algorithm Task 1: complete (commits 7cd9786..56f8760, review clean)
- Algorithm Task 2: complete (commits 56f8760..8d838ec, review approved; Minor: focused harness uses stubs, final project build must verify real-type integration)
- Algorithm Task 3: complete (commits 8d838ec..a66d48e, review clean)
- Algorithm Task 4: complete (commits a66d48e..3ab3d42, final re-review approved; Task 11 retains runtime identity/lifecycle wiring)
- Algorithm Task 5: complete (commits 615f481..0070303, third review approved; unified remains Shadow-only and malformed comparisons are contained)
- Algorithm Task 6: complete (commits 0070303..d2e4990, fresh review approved with Critical/Important/Minor all zero; season extensions require exact resolved build/season/card-pool provenance, and missing or incomplete simulator observations fail closed to value=0/coverage=0)
- Algorithm Task 7: complete (commits 9163eb1..9322b0e, final fresh re-review approved; Critical/Important/Minor all zero; fixed package SHA-256 BED76B33CA50B2D17B27ECF5BC1927D4D2F2CD4AD7A1BCE5EFEB6F0992D69D84, 74568 bytes)
- Algorithm Task 8: complete (commits 4922095..3131c56, final fresh re-review approved; Critical/Important/Minor all zero; fixed package SHA-256 8D23732DE768413105A11E9260C72EA77CA37F71ECFF15668D27040679516BE6, 74775 bytes)
- Algorithm Task 9: complete (commits 4583189..89429b9, final fresh re-review approved; Critical/Important/Minor all zero; fixed package SHA-256 90785E8492BE6236BEF7454FF4305E389A01EA4EE26373A9E51281C6EABD51E9, 67769 bytes; calibration and approval are bound to exact model identity, and malformed feature vectors fail closed)
- Algorithm Task 10: complete (commits a387f8e..21f47bd, final fresh re-review approved; Critical/Important/Minor all zero; fixed package SHA-256 A5430A11D5237C9286C9F4829B9D536E1315AE076162B7F5D93C5B71B79FFAE7, 76990 bytes; beam planning remains deterministic, bounded, score-independent beyond ranked roots, diagnostic-only, and fails closed for unavailable or malformed continuations)
- Algorithm Task 11: complete (commits 3282c33..4f3f638, third fresh review approved; Critical/Important/Minor all zero; fixed package SHA-256 B0A98ACB9DC898A81AA3B1FD40F1C7E67C0AD7EE6DE4A13A6FDBC0A20D8B00B3, 143857 bytes; production approval fails closed, unified remains Shadow-only, and legacy-v1.1.0 remains player-visible and fallback)
- Algorithm Task 12: complete (commits cc3dc48..f0b9f88, final fresh review approved; Critical/Important/Minor all zero; fixed package SHA-256 A57A3C4BF7C71D8C8F6C871D51047DDA7A6F47F9471DB0D4FEAFFBBACAD50C00, 325815 bytes; effect acceptance remains `blocked-by-season-data`, Unified ranking remains Shadow, and `legacy-v1.1.0` remains player-visible and the fallback)
- Final whole-branch review: complete (range `0070303..07f9d23`; fresh reviewer approved with Critical 0, Important 0, Minor 2; fixed package SHA-256 `6358391297CFF01FF5962E1917870FB0F4E9ACF5FFF1A7C380A13DCC75330107`, 775344 bytes)
- Final validation and branch handoff: complete for code HEAD `07f9d23` (six focused checks, full `npm test`, offline runtime, independent Release build, repository validation, and whitespace checks passed; final documentation-only commit and post-commit checks are recorded in the external handoff)

Final validation used the exact HDT 1.54.0.7376 reference directory. Full `npm test` exited 0 in approximately 309.5 seconds with 140 `PASS` lines. The independent Release build produced `BobCoach.dll` at 784896 bytes with SHA-256 `8BF904C1CCC3B9CF6C2297005410E833987354AFA0E6968B395654B3B9C4BE56`. Repository validation scanned 451 tracked or untracked non-ignored files and passed. Evidence is under `E:\HDT-BobCoach-evidence\verification\whole-final-07f9d23-20260801-185035` and `E:\HDT-BobCoach-evidence\builds\whole-final-07f9d23-20260801-185035`.

Residual Minor findings: the Task 12 report was stale inside the reviewed package and is synchronized by the final documentation commit; `tests/test_algorithm_model_contract.ps1` still has an author-machine `E:`-drive fallback when `BOBCOACH_HDT_DIR` is unset, so portable runs should set that variable explicitly. One test temporary directory remained on `C:` after the final suite (`1147` files, `391` directories, `88959304` bytes). The owner later authorized deletion of that exact directory; immediately before cleanup it contained `887` files, `271` directories, and `79065116` bytes. Cleanup removed the remaining read-only `log.config` files and verified that the directory no longer exists. These findings do not change effect acceptance: it remains `blocked-by-season-data`, Unified remains Shadow, and `legacy-v1.1.0` remains player-visible and the fallback.
