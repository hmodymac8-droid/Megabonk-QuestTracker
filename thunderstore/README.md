# Quest Tracker

*Stop alt-tabbing to the quest menu. Pin a quest and watch it live while you bonk.*

## What it does

- **Track quests from anywhere**: **double-click** a quest in the main-menu quest list, a **locked skin** in character select, or a locked item in the Unlocks shop. Or hover any quest and press **T**.
- **Live progress**: "10,840 / 12,500 · 1,660 left · +312 this run"
- **Countdown timers** for timed quests (like Boss Buster): starts when the boss spawns, turns yellow then red, and tells you if you made it.
- **Pass / fail status**: ON TRACK, HURRY!, FAILED (took damage, time's up), COMPLETE!
- **Quest list (F7)**: search all quests and track them from anywhere.
- **Move and resize it**: press **F8**, drag the panel, scroll to resize.
- Track up to **3 quests** at once (configurable). Tracked quests are remembered between sessions.
- Failed and completed quests disappear after a few seconds. Failed ones come back on your next stage or run.
- Speaks Arabic automatically when used with **Megabonk Arabic**.

## Quest guide (new in 1.3.0)

Helps you finish your tracked quests without spoiling them:

- **Where to play**: the tracker shows the map, tier and stage a quest needs (only when it needs them), and warns you when a run won't count.
- **How to do it**: hidden and multi-step quests get a short **How:** line (Spooky Steve, Big Bob, the frogs, Monke, the crypts, pumpkins...).
- **Compass**: during a run, a small arrow around your character points to the next step, but **only to places you have already found**. Until then it just says what to look for ("Find the Boss Portal"). No spoilers, no cheating.
- Inside a crypt it leads you to the exit first.
- **Timed boss quests** tell you to find the Boss Portal and warn you when it's time to summon the boss.
- **F10** shows or hides the guide.

## Change your keys (new in 1.3.0)

Press **F7**, then **Keys**: click an action and press the new key. Keys the game uses are refused, and two Quest Tracker actions never share a key.

## Multiplayer safe

Quest Tracker only **reads** quest info and draws a panel and arrows. It never changes stats, spawns, items or RNG, so it works alongside **BonkWithFriends** and **MegabonkTogether**. Only you see the panel, and your friends don't need it installed.

## Controls

| Key | Action |
| --- | --- |
| Double-click | Track / untrack a main-menu quest, locked skin or locked unlock |
| T | Track / untrack the quest you're hovering |
| F7 | Open the quest list |
| F8 | Move / resize the tracker (drag, scroll, R to reset, Esc when done) |
| F6 | Show / hide the tracker |
| F9 | Export all quests to `BepInEx\QuestTracker_quests.tsv` (for bug reports) |
| F10 | Show / hide the quest guide |

Change keys in game with **F7 > Keys**. All keys, size and transparency are also in `BepInEx\config\questtracker.megabonk.cfg`.

## Update notice

Once per launch it checks for a newer version and shows a small notice in the menus (never during runs). Turn it off with `CheckForUpdates = false` in the config.

## Install

Install with **r2modman** or **Gale** (BepInEx is installed for you), or manually: put the `plugins/QuestTracker` folder into `BepInEx\plugins`.

## Credits

Special thanks to **sensei**, my friend and teacher.
Thanks to **pinkpomelo**, the **BepInEx team** (mod loader), **Il2CppInterop** (IL2CPP bridge), and **Harmony** by Andreas Pardeike (pardeike).
