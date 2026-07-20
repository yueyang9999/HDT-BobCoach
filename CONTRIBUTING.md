# Contributing

## Before opening a change

Use an Issue to describe behavior changes, new network endpoints, new stored data, or HDT compatibility changes before implementation. Small documentation and test corrections may go directly to a pull request.

Never attach raw `Power.log` files, private replays, account identifiers, credentials, tokens, personal paths, or third-party data dumps. Test fixtures must be synthetic and minimal.

## Development setup

Requirements and exact commands are documented in [docs/maintainer/BUILD.md](docs/maintainer/BUILD.md). Production code belongs in `src/BobCoach`, tests in `tests`, deterministic tooling in `tools`, and public documentation in `docs`.

Before submitting a pull request, run:

```powershell
npm test
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build\validate_repository.ps1
git diff --check
```

Behavior changes require focused tests. Do not suppress build errors, relax the 11-file package allowlist, or add mandatory network dependencies to make a test pass.

## Pull requests

- Keep one coherent change per pull request.
- Explain user-visible behavior and privacy impact.
- State the Windows and HDT versions used for validation.
- Update `CHANGELOG.md` for user-visible changes.
- Update `PRIVACY.md` and `DATA_SOURCES.md` before adding any endpoint, stored field, or data source.
- Do not include generated DLLs, ZIPs, screenshots, VM images, or validation evidence.

By submitting a contribution, you confirm that you have the right to license your original contribution under the repository's MIT License. Third-party content must retain its own license and must not be copied into this repository without a documented redistribution basis.
