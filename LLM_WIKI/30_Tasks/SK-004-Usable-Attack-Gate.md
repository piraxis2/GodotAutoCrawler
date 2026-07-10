---
id: SK-004
type: task
status: complete
system: Skill
created: 2026-07-10
updated: 2026-07-10
tags: [task, combat, skill-system, behavior-tree]
---

# SK-004 Usable Attack Gate

## Goal

제한 스킬의 ammo 또는 mana가 소진된 뒤에도 캐릭터가 이미 쓸 수 없는 주력기 사거리만 바라보다 멈추는 문제를 해결한다.

## Root Cause

`CharacterArticle.CalculatedAttackRange`가 BT에 연결된 모든 `BehaviorTree_TurnAction`의 사거리 중 가장 큰 값을 캐시/선택했다. 그래서 `staff_chainlightning`처럼 긴 사거리 제한 스킬이 ammo를 모두 써도 이동 판단은 여전히 그 사거리를 기준으로 했고, 실제 실행은 실패하면서 기본기 사거리까지 접근하지 못했다.

## Result

- `TurnActionBase.CanStart(CharacterArticle caster)` 계약을 추가했다.
- `TurnAction_Skill.CanStart`는 `SkillDefinition` 유효성, ammo remaining, mana affordability를 확인한다. 무대상 여부는 위치가 계속 변하는 런타임 판단이라 기존 `Init` 커밋 게이트가 맡는다.
- `CharacterArticle.HasUsableAttack`과 동적 `CalculatedAttackRange`를 추가/정리했다. 이동 판단은 현재 시작 가능한 `BehaviorTree_TurnAction`만 후보로 삼고, 가장 넓은 usable 사거리를 사용한다.
- `BehaviorTree_HasUsableAttack` decorator를 추가했다. `Invert` 옵션으로 공격 가능/불가능 분기를 만들 수 있다.
- BT graph context menu에 `Add HasUsableAttack (Decorator)` 항목을 추가했다.
- `Sk004Step1UsableAttackTest`를 추가해 spent ammo/mana-blocked 주력기가 이동 사거리에서 제외되고 무제한 기본기가 fallback 사거리로 쓰이는 계약을 고정했다.

## 2026-07-10 follow-up fix

`Sk002Step1AmmoTest`가 `Articles/Opponent/Character` 고정 경로에 의존해, 현재 `battle_field.tscn` 상대 노드명이 바뀌면 테스트가 실패했다. 테스트를 첫 번째 살아있는 상대 `CharacterArticle`을 찾는 헬퍼로 바꾸고, 상대 목록 순회도 `OfType<CharacterArticle>()`로 완화했다. 제품 동작 변경은 없다.

## Verification

- `dotnet build AutoCrawler.sln -c Debug`: PASS, warnings 0, errors 0.
- `sk002_step1_ammo_test`: ALL PASS.
- `sk004_step1_usable_attack_test`: ALL PASS.
- `sk002_step3_reset_test`: ALL PASS.

## Follow-ups

- 공격 불가능 상태에서 도망/거리 벌리기/대기 등을 선택하는 실제 BT 분기는 별도 Step으로 설계한다. 이번 Step은 판단 노드와 usable range 계약까지다.
- 기존 하드코딩 TurnAction의 데이터 스킬 재배선은 SK-001/SK-002 후속 범위다.

## Related

- [[Skill-System]]
- [[BehaviorTree-System]]
- [[SK-003-Skill-Based-Character-Scene]]
- [[ADR-021-Skill-Ammo-System]]