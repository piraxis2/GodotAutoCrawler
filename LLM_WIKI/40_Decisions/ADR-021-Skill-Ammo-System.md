---
id: ADR-021
type: decision
status: accepted
date: 2026-07-10
updated: 2026-07-10
system: Combat
---

# Skill Ammo System

## Context

2026-07-09 기획 정정으로 스킬에 `ammo`(스킬별 고정 보장 사용 횟수)를 도입한다. 이는 SK-002가 다루며, 본 ADR은
그 데이터 모델·runtime owner·소모 순서·reset scope·report/API 경계를 결정한다.

이 결정과 기존 문서의 관계를 정확히 둔다:

- [[ADR-018-Data-Driven-Skill-System]]은 ammo에 대해 **침묵했다(deferral)**. ADR-018 본문에는 탄수/ammo
  결정이 없다. 따라서 본 ADR은 ADR-018 결정을 뒤집는 것이 아니라, ADR-018이 미룬 축을 채운다.
- "WC(위저드 클라이머)식 플레이어 배분형 탄수는 채택하지 않는다"는 기획서/[[Skill-System]] Ammo Follow-up
  절의 결정이었다. 본 ADR은 그 배분형 폐기를 유지하되, AutoCrawler식 **디자이너 고정형 ammo**를 채택한다.
- 본 ADR은 ADR-018의 핵심 계약과 정합한다: 실행 중 변하는 값은 `SkillDefinition`(불변 Resource)이 아니라
  유닛별 runtime 상태가 소유한다. ammo remaining도 동일하게 Resource 밖에 둔다.

현재 코드 사실(2026-07-10 대조):

- `SkillDefinition : Resource`에 ammo 필드가 없다(`Assets/Script/SkillSystem/SkillDefinition.cs`).
- `TurnAction_Skill.Init`은 대상 확정 **이전에** 마나를 지불하고, 무대상이면 fail-closed하지 않고 턴/마나를
  소모하는 no-op으로 진행한다(`Assets/Script/SkillSystem/TurnAction_Skill.cs:30`~`:44`).
- `SkillState`(유닛별 실행 상태, non-Resource)가 phase queue/used cost/target/Reports를 소유한다.
- `CharacterArticle`이 `StatusController { get; } = new()`(plain-C#, 비직렬화, 유닛별)와
  `CurrentTurnActionState`를 소유한다(`Assets/Script/Article/CharacterArticle.cs:17`~`:18`).
- 지불 실패는 `ActionState.Failure` → `BtStatus.Failure`로 매핑돼 BT Selector fallback을 탄다
  (`CharacterArticle.cs:82`).

## Decision

### 1. Ammo는 Definition의 정책 데이터, remaining은 유닛별 runtime 상태

`SkillDefinition`에 additive·backward-compatible 필드를 추가한다.

```csharp
public enum SkillAmmoResetScope
{
    Unlimited,   // ammo counter 없음. 기본기용. 기존 .tres 기본값.
    Battle,      // 전투 시작마다 Ammo로 충전.
    Expedition,  // 예약값. 등반 lifecycle 도입 전까지 배선하지 않는다.
}

// SkillDefinition에 추가
[Export] public SkillAmmoResetScope AmmoResetScope { get; set; } = SkillAmmoResetScope.Unlimited;
[Export] public int Ammo { get; set; } = 0;
```

- 기본값 `Unlimited` + `Ammo = 0` → 기존 `.tres`가 무제한처럼 로드돼 SK-001 baseline을 보존한다.
- `IsValidV0`에 `Ammo >= 0` 검증 절을 추가한다. `AmmoResetScope`는 `Enum.IsDefined`로 검증한다.
- remaining ammo는 `SkillDefinition`에 **저장하지 않는다**(공유 Resource 오염 금지).

### 2. Runtime owner는 CharacterArticle이 소유하는 plain-C# `SkillAmmoState`

`StatusController`와 동형으로 둔다. Godot Resource가 아니고, `.tres`에 직렬화되지 않으며, 유닛별로 존재한다.

- `CharacterArticle`이 `SkillAmmoState`(가칭)를 소유한다.
- `SkillDefinition.Id`로 키잉해 `remaining / max / reset_scope`를 추적한다.
- `Unlimited` 스킬은 counter를 만들지 않는다(조회 시 "무제한"으로 응답).
- `TurnAction_Skill`은 `Definition.Id`로 caster의 ammo state를 조회·차감한다.
- 이로써 같은 `SkillDefinition`을 여러 유닛이 공유해도 remaining이 섞이지 않는다.

### 3. 소모 순서: target-first, 커밋(=시전 시작) 시 동시 소모, 이후 빗나감·취소는 환불 없음

**전제조건(런타임 Failure 게이트 아님):** definition/caster 유효성. `TryGetCaster` 실패, `Definition == null`,
`!IsValidV0()`은 **런타임 전술 실패가 아니라 저작/구조 오류**다. 이 경우 `Init`은 상태를 만들지 않고 그대로
반환하며, 이후 `Action`은 `SkillState`가 없어 기존대로 `ActionState.End`를 반환한다(SK-001 계약 유지).
이유는 두 가지다. ① 이들은 데이터/배선 버그이므로 BT fallback으로 조용히 삼키면 오히려 오류를 감춘다.
② `Failure`는 `caster.CurrentTurnActionState.Failed` 채널로만 전달되는데, caster 조회 자체가 실패하면 상태를 붙일
caster가 없어 상태 기반 `Failure`를 낼 수 없다. 따라서 유효성은 게이트가 아니라 진입 전제조건으로 둔다.

유효성을 통과한 뒤 `TurnAction_Skill.Init`의 **런타임 게이트**를 다음 순서로 재정렬한다. 아래 셋만 무소모
`Failure`(BT fallback)를 낸다.

1. ammo 제한 스킬이면 `remaining > 0` → 부족 시 무소모 `Failure`
2. 마나 지불 **가능성**(`Mana.CanAfford`)만 확인 → 부족 시 무소모 `Failure`
3. 대상 확정/락온(Self는 caster) → **시전 시작 시점에 사거리 내 유효 대상이 하나도 없으면** 무소모 `Failure`
4. 커밋(시전 시작 = 대상 락온 순간)에서 ammo 1 + 마나를 **함께** 소모
5. 커밋 이후는 환불하지 않는다:
   - 여러 턴 windup(장전) 중 락온한 대상이 이동/사망해 착탄 시 빗나가도 ammo/마나 **환불 없음**.
   - windup 중 스턴 취소(`CancelCasting`)도 **환불 없음**.

**설계 의도(사용자 확정):** 소모의 기준점은 착탄이 아니라 **시전 시작(대상 락온)**이다. 장전형 스킬이 락온한
위치로 발사했지만 그 사이 적이 빠져나가 빗나가는 것은 버그가 아니라 **의도된 전황(戰況)의 일부**다. 따라서 한 번
커밋된 ammo/마나는 결과(명중/빗나감/스턴 취소)와 무관하게 되돌리지 않는다. "발사 전에 아예 조준할 대상이 없는"
경우(§4의 무대상)만 커밋 이전 게이트로 걸러 무소모 `Failure`를 낸다.

이는 기존 마나-first(대상 확정 전 `TrySpend`) 순서를 바꾼다. 근거:

- "조준할 대상조차 없는데 탄/마나가 사라지는" 억울함을 제거하고 그 케이스만 BT fallback으로 돌린다.
- 반면 락온 후 빗나감은 손실로 남겨 "시전이 시작되면 결과에 책임진다"는 긴장을 살린다(사용자 의도).
- ADR-018 Follow-up이 남긴 "무대상 fail-closed/마나 환불" 미결을 이 방향으로 확정한다.
- 커밋 후 미환불은 `CancelCasting`의 기존 "지불 비용/마나 미환불" 계약과 일치한다(ADR-018 §9,
  `CharacterArticle.cs:26`~`:31`).

**착탄 시 재조준 금지:** 대상은 `SkillState.ConfirmedTargets`에 시전 시작 시점 후보로 락온되며, 착탄 phase가
대상을 재선택하지 않는다(현재 구조 유지). 이 락온-후-빗나감 경로가 위 의도를 성립시킨다.

**baseline 경계:** Phase1 기본기는 `ManaCost == 0` + `Unlimited`이고 baseline 테스트는 유효 대상을
세팅하므로, 이 재정렬은 해당 경로에서 no-op이다(RNG/HP 스트림 보존). 유일한 실질 변화는 "시전 시작 시점에 사거리
내 대상이 아예 없는" 케이스가 기존 마나 소모 no-op → 무소모 `Failure`로 바뀌는 것이다. 이는 신규 동작으로 Step 1
완료 조건에 명시한다.

### 4. Reset scope 최소 집합은 {Unlimited, Battle}, Expedition은 예약

- Step 1~3은 `Unlimited`와 `Battle`만 배선한다.
- `Battle`: 전투 시작 시 제한 스킬 remaining을 `Ammo`로 충전한다. 전투 사이 이월 없음.
- `Expedition`은 enum 예약값으로만 둔다(ADR-018 `combo` 예약 선례). 현재 등반/출전 lifecycle이 없으므로
  Phase1 `.tres`는 사용하지 않고, 런타임이 `Expedition`을 만나면 이월 없는 `Battle`로 안전 처리하며 그
  divergence를 문서화한다. 등반 lifecycle 도입 시 실제 이월을 붙인다.
- charge 소스: 유닛 BT의 `TurnAction_Skill` Definition 순회를 loadout 대역으로 사용한다(기존
  `AttackRangePositions`가 이미 BT를 순회, `CharacterArticle.cs:42`). 장착/준비 UI는 out of scope다.
  Step 1은 테스트 헬퍼가 caster에 ammo state를 직접 주입해 우회한다.

### 5. 저장/이월 경계

- ammo remaining은 **runtime 메모리에만** 존재한다. `SkillDefinition` Resource에도, `.tres`에도 쓰지 않는다.
- Step 1~3에서 전투 밖 영속(save/load) 통합은 out of scope다. Expedition 이월과 save 통합은 후속.

### 6. Report/API 최소 정책

- **report:** `SkillState.Reports`(`List<string>`)에 소모 시 1줄, 부족 실패 시 1줄만 append한다. 구조화
  어휘와 GL-001 어댑터 정리는 후속(Task Risk).
- **읽기 API:** ammo owner에 `bool TryGetAmmo(StringName skillId, out int remaining, out int max)`를 둔다.
  `Unlimited`/미보유는 false 반환. 읽기 전용, 변이 없음. HUD는 구현하지 않되 읽을 창구만 확보한다.

## Rationale

- Definition을 불변으로 유지하면 공유 Resource 오염 없이 유닛별 ammo를 격리할 수 있다(ADR-018과 동일 논리).
- `StatusController` 선례를 따르면 새 추상화 없이 검증된 owner 패턴을 재사용한다.
- target-first 소모는 무대상을 BT fallback으로 정리하고, 기본기 무제한 덕에 자원 부족만으로 전투가 멈추지 않는다.
- 최소 reset scope는 미완성 등반 lifecycle과의 조기 결합을 피한다.

## Consequences

### Positive

- 신규 스킬의 ammo 밸런싱이 `.tres` 편집으로 끝난다.
- 무대상 마나 스킬이 마나를 낭비하지 않고 다음 BT 행으로 넘어간다.
- ammo 상태가 Resource 밖에 있어 공유 사고를 테스트로 조기에 잡는다.

### Negative

- 마나 지불 순서 재정렬로 "무대상 마나 스킬" 관찰 동작이 바뀐다(신규 Failure). Step 1 완료 조건·테스트로 고정 필요.
- `Expedition`이 예약값으로 남아 미배선 divergence를 문서로만 관리한다.
- ammo 영속(save/load)이 후속으로 남아, 전투 밖 상태 보존은 v0에서 제공하지 않는다.

## Verification

- 신규 필드 `.tres` 저장/재로드 round-trip 보존.
- `Unlimited` 스킬 baseline(상태열|hp|next_rng|anim) 도입 전후 일치.
- 제한 스킬 remaining 1회 차감 후 실행 / remaining 0이면 무소모 `Failure`.
- 마나 부족·무대상에서 ammo·마나 미차감 + `Failure`.
- windup 스턴 취소에서 이미 차감한 ammo 미환불.
- shared `SkillDefinition` + per-unit ammo 격리.
- `Battle` reset 충전 / `Expedition` 미배선 안전 처리.
- SK-001 step1~6, ST-001 마나, CB-001 결정론 회귀 유지.

## Follow-ups

- Expedition 이월과 save/load 영속 통합(등반 lifecycle 도입 후).
- 장착/준비 UI와 플레이어 loadout 소스(현재는 BT Definition 순회 대역).
- 전투 HUD ammo 표시(읽기 API 소비).
- `SkillState.Reports` 구조화 어휘의 GL-001 어댑터 정리.

## Related

- [[SK-002-Skill-Ammo-System]]
- [[SK-002-Skill-Ammo-System-Review]]
- [[ADR-018-Data-Driven-Skill-System]]
- [[Skill-System]]
- [[ADR-020-GameLog-Service-Lifetime]]
