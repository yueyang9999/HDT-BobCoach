## Summary

Describe the user or maintainer impact.

## Validation

- [ ] Ran focused tests and `npm test` where the environment permits.
- [ ] Ran `tools/build/validate_repository.ps1` when available.
- [ ] Ran `git diff --check`.
- [ ] Did not add DLLs, ZIPs, logs, replays, screenshots, VM files, secrets, or personal paths.

## Boundary review

- [ ] No mandatory network dependency or user-data upload was added.
- [ ] `PRIVACY.md` and `DATA_SOURCES.md` were updated before any storage, endpoint, or source change.
- [ ] All player-facing functionality remains free; no support mechanism unlocks features or appears in Hearthstone/HDT overlay.
- [ ] No third-party logo or endorsement claim was added.
- [ ] This pull request does not create or authorize a GitHub Release.
