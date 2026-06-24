# BT-001 Step 5 Battle Debug Walkthrough

Date: 2026-06-20

## Automated Verification

- `dotnet build AutoCrawler.sln -c Debug`
  - Result: PASS, 0 warnings, 0 errors.
- `D:\SteamLibrary\steamapps\common\Godot Engine\Godot_v4.6.3-stable_mono_win64_console.exe --headless --path . res://addons/behaviortree/tests/bt_validation_test.tscn`
  - Result: PASS, exit 0.
  - Coverage: A~T PASS.
  - New Step 5 coverage:
    - S: two remote `tree_path` tabs route tick payloads independently.
    - T: closing one remote tab sends stop for only that tree and leaves the other tab intact.
  - Notes: existing `SalvageChildren` owner warnings and an ObjectDB leak warning still print after PASS; they are not introduced by Step 5.

## F5 Battle Smoke

Scene:

- `Assets/Scenes/Map/battle_field.tscn`

Steps performed:

1. Launched the Godot editor with `battle_field.tscn`.
2. Sent F5 to start play.
3. Observed a Godot play process start.
4. Observed `🌵Behavior Tree Editor` open automatically after runtime `behavior_tree:register`.
5. Confirmed the discovery selector shows a live tree with full path:
   `Character — /root/BattleField/Articles/Ally/Character/BehaviorTree`.
   - Screenshot: `walkthrough_step5_discovery.png`
6. Clicked `Start`.
7. Confirmed a remote tab was created and the payload-built graph displayed selector/decorator/action nodes and connections.
   - Screenshot: `walkthrough_step5_after_dispatch_fix_start.png`
8. Clicked `Stop`.
9. Confirmed the tab stayed open and changed to `[STALE]`; node elapsed labels also gained `[STALE]`.
   - Screenshot: `walkthrough_step5_after_stop_stale.png`

Observed limitations:

- The current `battle_field.tscn` registers only one live BehaviorTree in discovery. Opponent Puppet instances do not appear as remote BehaviorTree targets in this smoke run, so second-character manual Start and visual multi-tab routing could not be observed from this scene.
- The graph appeared and stayed stable, but visible Success/Failure/Running color changes and non-zero elapsed values were not observed during this run. Automated tests P and S cover status color/highlight and routing isolation with deterministic payloads.
- Character death/delete stale via natural combat was not observed. Manual Stop stale was observed; unregister stale remains covered by R and payload graph stale behavior by P/R.

Screenshots kept:

- `walkthrough_step5_discovery.png`
- `walkthrough_step5_after_dispatch_fix_start.png`
- `walkthrough_step5_after_stop_stale.png`
