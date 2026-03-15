# PR Title

[chore] Align docs and enforce markdown links

# PR Description

This audits the repo’s documentation against the current codebase and fixes the main drift points instead of papering over them. It adds a repo-local markdown link checker, wires it into CI, and codifies the rule that in-repo file references in prose must be actual markdown links rather than dead backticked paths.

It also updates the main navigation hubs, rewrites the stale CLI and workspace architecture docs against the live implementation, fixes outdated paths and capability claims across the language, editor, package, and test docs, and marks the ACIR-era RFCs as historical context instead of current source of truth. Verification for this patch was `python3 scripts/check_markdown_links.py`, `dotnet build tools/cli/Cascode.Cli.csproj`, and `dotnet test Cascode.sln --configuration Release`.
