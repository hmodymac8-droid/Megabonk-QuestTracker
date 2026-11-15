QUEST TRACKER for Megabonk
==========================

Track any quest live while you play: progress, kills left, countdown timers,
and whether you're on track, failing, or done.

HOW TO TRACK A QUEST (any of these)
- Main menu quest list: hover a quest and DOUBLE-CLICK it (or press T).
- Character select: hover a LOCKED skin and DOUBLE-CLICK it (or press T) to track the quest that unlocks it.
- Unlocks shop: hover a locked item / weapon / tome and DOUBLE-CLICK it (or press T).
- Quests menu: hover a quest and press T (clicking there claims rewards, so use T).
- Anywhere: press F7 to open the quest list, search it, and click "Track".
Do the same again to untrack. You can track up to 3 quests at once (changeable in the config).

QUEST GUIDE (new in 1.3.0)
Helps you finish your tracked quests without spoiling them:
- WHERE TO PLAY: the tracker shows the map, tier and stage a quest needs (only when it needs them),
  and warns you in a run that won't count ("Needs Graveyard - Tier 2").
- HOW TO DO IT: hidden and multi-step quests get a short "How:" line (Spooky Steve, Big Bob, the frogs,
  Monke, the crypts, pumpkins...).
- COMPASS: during a run, a small arrow around your character points the way to the next step, but only
  to places you have already found yourself. Until then it just says what to look for ("Find the Boss Portal").
  It never reveals hidden places, the Boss Portal or the exit portal before you discover them.
- Inside a crypt it leads you to the exit first.
- Timed boss quests ("kill the boss in under 6 minutes") tell you to find the Boss Portal and warn you
  when it's time to summon the boss.
- F10 shows or hides the guide.

CHANGE YOUR KEYS (new in 1.3.0)
Press F7, then "Keys". Click an action and press the new key. Keys the game uses are refused,
and two Quest Tracker actions never share a key.

MOVE AND RESIZE THE TRACKER
Press F8 (or "Move / Resize" in the F7 list):
- Drag the panel to move it.
- Scroll the mouse wheel, press +/-, or drag the gold corner to resize.
- Click the red button on a quest to untrack it.
- R resets to default. Esc or F8 when done. It's saved automatically.

FAILED AND COMPLETED QUESTS
They disappear from the panel after 8 seconds so they don't clutter your screen.
A failed quest stays tracked and comes back on the next stage or run.
Change the delays (or set 0 to never hide) in the config: HideFailedAfterSeconds / HideCompletedAfterSeconds.

OTHER KEYS
- F6 shows or hides the tracker.
- F9 exports every quest to BepInEx\QuestTracker_quests.tsv (useful for bug reports).
- F10 shows or hides the quest guide.

SETTINGS
Change keys in game: F7 > Keys. All keys and options are also in BepInEx\config\questtracker.megabonk.cfg (created on first launch).

MULTIPLAYER
The mod only reads quest info and draws a panel and arrows. It never changes the game,
so it's safe to use with BonkWithFriends / MegabonkTogether. Only you see the panel.

UNINSTALL
Delete the folder BepInEx\plugins\QuestTracker

CREDITS
Special thanks to sensei, my friend and teacher.
pinkpomelo, BepInEx team (mod loader), Il2CppInterop (IL2CPP bridge), Harmony by Andreas Pardeike (pardeike).
