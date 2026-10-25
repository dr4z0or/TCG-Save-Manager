# Changelog

## 1.4.2
- Fixed an endless "quit and relaunch the game" loop when loading a shop with a version of Shop Lot C, Upstairs, Furniture Overhaul or Staff Overhaul that Save Manager does not know yet. The first shop loaded after starting the game now always loads directly; a restart is only asked when switching from one shop to another in the same session, and that restart now works.

## 1.4.1 — first public release
- Import only offers real game saves: files merely named like a save (test data, truncated copy) are ignored, so they cannot become a shop the game fails to load.
- A damaged backup save no longer blocks saving snapshots or loading the shop.

## 1.4.0
- **Import from any folder**: *Other folder…* in the import window opens a folder browser (drives, USB keys, recent folders). Folders containing saves are highlighted; the chosen folder is only read.
- Before importing, each slot shows the mods its save uses (Staff Overhaul, Shop C, Upstairs, Furniture Overhaul, Enhanced Prefab Loader, Collection Tracker) and which ones are missing.
- A shop whose saves need mods that are not installed shows "Missing mods: …" on its card.

## 1.3.0
- Interface in English or French (follows the game's language; setting `Interface.Language`).
- *Import a save* reads the game's own save folder (left unchanged) when Save Manager is installed in the real game.
- With an untested version of Furniture Overhaul or Staff Overhaul, the chosen shop is loaded at the next start instead of being refused.
- The F8 shortcut is removed: the manager opens from the game's Load and Save menus (an optional shortcut can be set in `Interface.ToggleKey`).

## 1.2.0
- Binder cards of Enhanced Prefab Loader editions and Collection Tracker counters saved with each shop.

## 1.1.0
- 3 named manual slots per shop, previous version kept once, Continue button, shop deletion.
