<div align="center">

# CPPAN
### Comprehensive People Playground Archive Network

**A safer mod manager for People Playground, built after the Workshop went dark.**

![platform](https://img.shields.io/badge/platform-Windows-blue) ![.NET](https://img.shields.io/badge/.NET-8-purple) ![status](https://img.shields.io/badge/status-early%20development-orange)

</div>

---

## Why this exists

People Playground's modding scene took two serious hits in 2026.

- **February 2026:** a malicious mod called *FPS++* appeared on the Steam Workshop. It copied itself over other mods and wiped players' saved builds. The Workshop was temporarily shut down.
- **September 2026:** a second, nastier self-replicating mod appeared. It republished itself, deleted files belonging to People Playground and other games, and sent slurs to victims' friends lists. The Workshop was disabled again, and the developers ended official support for C# mods while they work on a safer scripting system.

Hundreds of mods and years of community work were suddenly stuck behind a closed door. CPPAN is a community answer to that: **an open, checked, GitHub-based archive plus a manager that installs mods without blind trust.**

> The Workshop's closure was a reasonable response to a real attack. CPPAN is not a protest against it. It is an attempt to make modding survivable again.

## What CPPAN does

| Feature | What it means for you |
|---|---|
| **One-click install** | Browse, search and install mods from a single window styled like CKAN (the Kerbal Space Program mod manager). |
| **Safety scan on every install** | Packages are checked before extraction: zip-slip paths, zip bombs, `.exe`/`.bat`/`.ps1` files, native DLLs, and suspicious code such as deleting folders, network calls, process launching or Steam friends-list access. Windows Defender is run too when available. |
| **Hash verification** | Every download must match the SHA256 in the registry. A tampered file is rejected. |
| **Known-bad blocklist** | Hashes of confirmed malicious mods are banned for all users. |
| **Archive mirrors** | Mods that vanished from the Workshop can be preserved and still installed. |
| **Instances** | Point CPPAN at any `People Playground.exe` (or auto-detect Steam) and manage separate installs. |
| **People API (required)** | A core mod installed automatically on every instance and impossible to remove. It connects mods to the game and to each other. |
| **Dependencies and conflicts** | Install one mod and its requirements come with it. Conflicting mods are caught before they break your game. |
| **Launch your way** | Start the game through Steam or straight from the `.exe`. |
| **Modpacks** | Export your setup and import someone else's. |
| **Upload system** | Scan and package your own mod, then submit it for review. |
| **Auto-update** | CPPAN updates itself from GitHub Releases, and the mod list refreshes from the registry. |

## Install

1. Go to the [**Releases**](../../releases) page and download `CPPAN-win-x64.zip`.
2. Unzip it anywhere and run `CPPAN.exe`.
3. Click **Add (locate .exe)** (or **Detect Steam**) and pick your People Playground install.
4. CPPAN downloads the mod list and installs People API.
5. Tick the mods you want and click **Apply**, then **Play**.

## Submitting a mod

1. Open **Upload mod** in CPPAN and pick your zip or mod folder.
2. Fix anything the scan flags. Blocked packages cannot be submitted.
3. Click **Create submission package**. A GitHub issue opens; attach the two generated files.
4. A maintainer reviews it and adds it to the registry.

A real mod needs a `mod.json` with at least a `Name`. Random zips are rejected.

## Honest limits

- The scanner is a **safety net, not a guarantee.** It catches the behaviours seen in the 2026 attacks, but determined attackers can hide things from static checks. Human review of submissions is the real protection.
- CPPAN is **early development.** Expect rough edges.
- **People API is still being built.** The manager side is in place; the mod itself is in progress.
- CPPAN is **unofficial** and not affiliated with Studio Minus or the People Playground developer.

## For developers

```
dotnet run --project src/CPPAN
```

Requires the .NET 8 SDK on Windows. Pushing a tag like `v1.0.1` makes GitHub Actions build and publish a release, and installed copies of CPPAN offer the update.

The mod registry lives in the separate [`CPPAN-meta`](../../../CPPAN-meta) repository as a single `index.json`.

## Contributing

Bug reports, scanner rules, registry entries and pull requests are all welcome. If you find a malicious mod, report its SHA256 so it can be added to the blocklist.

---

<div align="center">Made by <b>nexentondev</b> for the People Playground community.</div>
