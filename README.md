# CPPAN - Comprehensive People Playground Archive Network
A CKAN-style mod manager for People Playground (WinForms, .NET 8).

## Put it on GitHub (two repos)
1. **App repo** `CPPAN`: create it on github.com (empty, no README), then in this folder:
   ```
   git init
   git add .
   git commit -m "CPPAN first commit"
   git branch -M main
   git remote add origin https://github.com/YOUR_USERNAME/CPPAN.git
   git push -u origin main
   ```
2. **Registry repo** `CPPAN-meta`: new repo containing `registry-example/index.json` as `index.json` at the root, branch `main`.
3. Edit `Config.Owner` in `src/CPPAN/Models.cs` to your GitHub username and commit.
4. **Release**: `git tag v1.0.0 && git push origin v1.0.0`. The Action builds `CPPAN-win-x64.zip` and publishes a Release.
5. **Auto-update**: CPPAN checks `/releases/latest` on launch; push a higher tag (v1.0.1) and every install offers the update.

## Build locally
Install the .NET 8 SDK on Windows, then `dotnet run --project src/CPPAN`.

## Adding mods to the registry
Package each mod zip as a GitHub Release asset on `CPPAN-meta` (tag `<id>-<version>`), compute its SHA256, and add its entry to `index.json`.
Submissions made with the in-app Upload window arrive as GitHub issues with the zip + `.cppan.json` attached.
Add known-bad hashes to `blockedHashes` to ban them for everyone.
