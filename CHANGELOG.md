## CPPAN 1.0.2

### Bugfixes
- **Old mods now install even without a `mod.json`.** A missing manifest no longer means "not a mod". CPPAN looks for old-style content (`.outline`, `.jaap`, a `content` folder) and scripts instead. Everything is still fully safety-scanned, and old-style mods that contain code get an extra warning.
- **Thumbnails:** if a mod has a picture in its main folder (and no `ThumbnailPath`), CPPAN shows it and asks whether it is the thumbnail. The thumbnail is shown in the details panel.

### Add-ons
- **Old mod support / porting:** mods made for 1.27 (or any older game version) are ported to your game version on install by updating their manifest; a generated `mod.json` is created for mods that had none. The original is kept as `mod.json.cppan-orig`. (Code is not rewritten: mods that depend on removed game features may still break.)
- **Version-aware instances:** each instance has an exact game version (e.g. `1.27.5`, `1.27.2`, `1.28.32`), auto-detected from the exe or folder name and editable. New search filters: `is:portable`, `is:native`, `is:compatible`.
- **Saved structures / contraptions:** packages that only contain saved structures are detected and installed into your Contraptions folder instead of Mods (folder configurable per instance).
- **Log:** every install has a Log tab explaining what CPPAN detected and changed, plus a global log (Help > Open CPPAN log). Mods installed from a file now appear in the list so they can be removed.

### People API 1.1.0
- Analyses every mod folder, sorts its files by purpose and applies *routes* (manifest, script, legacy-content, assets, contraption) - different ways of getting content into the game. Custom routes can be added.
- Aliases for renamed things in old mods; log file at `Mods/PeopleAPI_Data/peopleapi.log`.
