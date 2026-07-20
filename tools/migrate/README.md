# Migration Tools

This directory contains one-time, auditable repository migration helpers. They are not part of the product build or release package.

`copy_project_allowlist.ps1` copies only the files explicitly referenced by `BobCoach.csproj`, then verifies that every declared compile and resource item exists at the destination.
