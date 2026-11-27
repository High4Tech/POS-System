# Local build performance

The installed POS is independent of the SDK, Git checkpoints and archived build payloads. Do not keep generated payloads in a synced source workspace: background checkpointing, indexing and sync can process several gigabytes after every build.

`Build-LocalDesktop.ps1` and `Build-TestRetail.ps1` default to `%LOCALAPPDATA%\High4TechPosBuild\artifacts\windows\<Version>\desktop` and `test`. They reject OneDrive and source-checkout output paths. Explicit non-synced output locations remain supported. Existing output directories are still rejected, preserving prior payloads.

Desktop compilation uses one MSBuild worker, disables persistent build servers/shared compilation, and disables node reuse. Verification remains mandatory. No Windows security/indexing settings or POS scanning intervals are changed.

For example:

```powershell
./engineering/Build-LocalDesktop.ps1 -Version '1.2.0-beta.3' -Dotnet $dotnet -NoRestore
./engineering/Build-TestRetail.ps1 -Version '1.2.0-beta.3' -Dotnet $dotnet -Vpk $vpk -NoRestore
```

On the existing development PC, 8,162 generated files (6.37 GiB) were moved out of OneDrive to the local archive. Counts, total size and the current desktop/test-installer hashes were checked. The outer workspace ignores artifacts; the enclosing home checkpoint repository locally excludes only the POS build archive and SDK cache. Git history, backups, published testing installers and live store files were retained.

These are build/workspace changes. They do not require replacing the installed POS or interrupting pending transactions. Low-end hardware performance and peak compilation CPU remain separate measurements; single-worker builds do not impose a system-wide CPU cap.

## Verification and observed result — 27 November 2026

PowerShell parsing passed. A mock SDK invocation verified the default local output, single-worker/no-reuse/no-shared-server arguments, retention of mandatory payload verification, and rejection of OneDrive paths by both scripts before anything was created there. The real .NET CLI accepted the new verification options and verified the existing Release assembly without rebuilding it.

The final archive retained exactly 8,162 files and 6.37 GiB; current desktop/test-installer hashes matched after the move. The installed EXE/DLL and saved cart-draft hashes remained unchanged. One orphaned staging worker was stopped, and the SDK's MSBuild/compiler shutdown commands completed successfully. No persistent background cleanup task was introduced.

After cleanup, five machine CPU samples were 41.2%, 40.2%, 26.3%, 29.1% and 30.5%; the POS averaged 0.06%. The earlier audit observed 75.8–94.7% machine CPU and 0.08% POS CPU. Other app activity also changed (Firefox usage fell and Disk Cleanup was running), so this is an observed improvement, not a controlled attribution of the entire drop to these changes. Low-end hardware and peak compilation load were not benchmarked.
