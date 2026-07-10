---
id: SK-003
type: task
status: complete
system: Skill
created: 2026-07-10
updated: 2026-07-10
tags: [task, combat, skill-system, scene]
---

# SK-003 Skill-Based Character Scene

## Goal

최신 데이터 기반 스킬 시스템을 실제 캐릭터 씬 하나에 배선한다.

## Scope

- 새 캐릭터 씬 `Assets/Scenes/Character/SkillCaster.tscn`을 추가한다.
- 기존 캐릭터 씬의 상태/애니메이션/BT 구조를 재사용한다.
- legacy `TurnAction_ChainLightning` 대신 `TurnAction_Skill`을 사용한다.
- Phase1 지팡이 스킬 `staff_chainlightning`과 `staff_magicbolt`를 BT에 연결한다.

## Result

- `SkillCaster.tscn`은 `CharacterArticle` + `Mana`/`ManaRegen`/`HealthRegen` production status 구성을 가진다.
- BT root는 기존 이동 노드 뒤에 데이터 스킬 2개를 둔다.
  - `StaffChainLightning`: `Assets/SkillData/Phase1/staff_chainlightning.tres`, Battle ammo 1, windup 1, mana 30.
  - `StaffMagicBolt`: `Assets/SkillData/Phase1/staff_magicbolt.tres`, Unlimited fallback, mana 5.
- SK-002의 battle-start `ChargeBattleAmmo()`가 이 씬의 제한 스킬을 충전할 수 있다.
- 이 Task의 산출물은 `SkillCaster.tscn` 샘플 배선이다. 실제 encounter 배치와 밸런스/결정론 baseline 재검증은 씬 변경 범위에서 별도로 판단한다.

## 2026-07-10 follow-up fix

사용자 관찰: `SkillCaster`가 스킬을 한 번 시도한 뒤 다음 행동으로 자연스럽게 돌아오지 않는 것처럼 보였다.

수정:

- `TurnAction_Skill.Action()`이 Failure/End/Completed로 닫힐 때 `CurrentTurnActionState`뿐 아니라, 자신이 `CharacterArticle.CurrentTurnAction`으로 남아 있으면 함께 비운다.
- `Sk002Step1AmmoTest`의 empty ammo Failure 케이스에 stale `CurrentTurnAction` 정리 단언(`D.current_action_cleared`)을 추가했다.

검증:

- `dotnet build AutoCrawler.sln -c Debug`: PASS, warnings 0, errors 0.
- `sk002_step1_ammo_test`: ALL PASS, `D.current_action_cleared` 포함.
- `sk002_step3_reset_test`: ALL PASS.
- Godot `--headless --path . --import`: exit 0. 기존 종료 시 ObjectDB/resource-in-use 로그는 유지.

## Verification

- `dotnet build AutoCrawler.sln -c Debug`: PASS, warnings 0, errors 0.
- Godot 4.6.3 Mono `--headless --path . --import`: exit 0.
  - 기존과 같은 종료 시 ObjectDB/resource-in-use 로그가 남는다.

## Follow-ups

- ammo/mana 소진 후 이동 사거리 스턱은 [[SK-004-Usable-Attack-Gate]]에서 해결했다.
- ammo HUD/save/load/loadout UI는 SK-002 후속 범위다.

## Related

- [[Skill-System]]
- [[SK-002-Skill-Ammo-System]]
- [[ADR-021-Skill-Ammo-System]]