---
id: SK-002
type: task
status: complete
system: Skill
created: 2026-07-09
updated: 2026-07-10
tags: [task, combat, skill-system, ammo, resource]
---

# SK-002 Skill Ammo System

## Goal

스킬별 고정 보장 사용 횟수 `ammo`를 도입한다.

목표 상태:

- 각 `SkillDefinition`은 기본 ammo 정책을 가진다.
- 플레이어는 스킬별 발수를 직접 배분하지 않는다.
- 스킬을 장착/준비하면 정의된 ammo만큼 자동 충전된다.
- ammo가 0인 제한 스킬은 `ActionState.Failure`로 닫혀 BT fallback을 탄다.
- 기본기는 무제한으로 유지해 전투가 자원 부족만으로 정지하지 않는다.

## Starting Code Facts

- SK-001은 `SkillDefinition`, `SkillState`, `EffectBlock`, `TurnAction_Skill` 기반 데이터 스킬 시스템을 완료했다.
- 착수 당시 `SkillDefinition` 필드에는 `ManaCost`, `ActCost`, `WindupCost`, `Range`, `Effects` 등이 있었고 ammo 필드는 없었다. SK-002 완료 후 현재 사실은 [[Skill-System]] "Ammo" 절이 보존한다.
- 착수 당시 `TurnAction_Skill.Init`은 마나 부족 시 `SkillState.Failed = true`를 세우고, `Action`이 `ActionState.Failure`를 반환했다. SK-002에서 ammo/마나/무대상 target-first 게이트로 재정렬됐다.
- `BehaviorTree_TurnAction`과 `CharacterArticle.TurnPlay`는 `ActionState.Failure`를 `BtStatus.Failure`로 매핑해 `BehaviorTree_Selector` fallback을 지원한다.
- `SkillState`는 유닛별 실행 상태이며 `.tres` 저장 대상이 아니다.
- ST-001로 production 캐릭터에 `Mana`가 배선됐다. 다만 플레이어 production `ManaRegen` 수치는 기획과 충돌 가능성이 있어 별도 Open Task로 남아 있다.
- 기존 3종 하드코딩 TurnAction은 아직 일부 씬/BT에 남아 있고, 데이터 스킬로의 재배선/삭제는 SK-001 후속 cleanup이다.

## Source Design Summary

2026-07-09 기획 정정:

- 기존 "탄수 없음" 결정은 폐기한다.
- WC식 플레이어 배분형 탄수는 채택하지 않는다.
- AutoCrawler의 `ammo`는 **스킬별 고정 보장 사용 횟수**다.
- 수량은 디자이너가 `SkillDefinition`에 정하고, 플레이어는 어떤 스킬을 장착/준비할지만 선택한다.
- ammo는 준비한 전술 카드의 남은 사용 횟수, 마나는 전투 중 비용, 행동력은 시간 비용, 한계 턴은 전투 전체 시계로 역할을 분리한다.

## Scope

- `SkillDefinition`에 ammo 데이터 필드를 추가한다.
- runtime remaining ammo를 유닛별/전투별 상태로 보관한다.
- `TurnAction_Skill` 실행 게이트에 ammo 부족 Failure를 추가한다.
- 대상 없음/마나 부족/ammo 부족/영창 취소의 소모 정책을 테스트로 고정한다.
- Phase1 12종 `.tres`에 ammo 초깃값을 입력한다.
- `SkillReport` 또는 현 `SkillState.Reports`에 ammo 소모/부족 report를 남길지 최소 정책을 정한다.

## Out of Scope

- 플레이어가 스킬별 ammo 수량을 직접 배분하는 UI.
- 스킬 장착/준비 화면 전체 구현.
- 스킬 구매, 레벨 스케일링, 장비에 따른 ammo 증가/감소.
- 기존 하드코딩 TurnAction 삭제/BT 재배선.
- 한계 턴 구현.
- 전투 HUD ammo 표시. 단, 후속 UI가 읽을 수 있는 상태 API는 설계 범위에 포함할 수 있다.

## Proposed Architecture

### Data

초기 제안:

```csharp
public enum SkillAmmoResetScope
{
    Unlimited,
    Battle,
    Expedition,
}

public partial class SkillDefinition : Resource
{
    [Export] public SkillAmmoResetScope AmmoResetScope { get; set; } = SkillAmmoResetScope.Unlimited;
    [Export] public int Ammo { get; set; } = 0;
}
```

의미:

- `Unlimited`: ammo counter를 만들지 않는다. 기본기용.
- `Battle`: 전투 시작마다 `Ammo`로 회복한다.
- `Expedition`: 등반/출전 시작마다 `Ammo`로 회복하고 전투 사이에 이월한다.

구현 Step 1은 `Unlimited` + `Battle` 또는 `Unlimited` + `Expedition` 중 하나로 줄여 시작할 수 있다. Step 0 설계 리뷰에서 reset scope 최소 집합을 확정한다.

### Runtime State

`SkillDefinition`은 공유 불변 Resource이므로 remaining ammo를 직접 저장하지 않는다.

후보:

- `SkillAmmoState`: `skill_id -> remaining/max/reset_scope`를 가진 유닛별 상태 객체.
- `CharacterArticle` 또는 별도 `SkillLoadoutRuntime`이 ammo state를 소유한다.
- `TurnAction_Skill`은 `Definition.Id`로 현재 caster의 ammo state를 조회하고 차감한다.

초기 테스트에서는 장착/준비 UI 없이, 테스트 helper가 caster에 ammo state를 주입한다.

### Consumption Policy

권장 정책:

1. definition/caster 유효성 확인.
2. ammo 제한 스킬이면 `remaining > 0` 확인. 부족하면 소모 없이 Failure.
3. 마나 지불 가능성 확인. 부족하면 ammo 소모 없이 Failure.
4. 대상 확정. 대상 없으면 ammo/마나 소모 없이 Failure.
5. 시전 시작 확정 시 ammo 1회와 마나를 함께 소모.
6. 이후 windup 중 스턴 취소는 ammo/마나 환불 없음.

이 정책은 "대상도 없는데 탄환이 사라지는" 억울함을 막고, "시전이 시작된 뒤 끊기면 손실"이라는 D&D식 긴장을 살린다.

### Phase1 Initial Ammo

초안:

| Skill | Ammo |
| --- | --- |
| sword_slash / fist_jab / bow_shot / staff_magicbolt | Unlimited |
| sword_smash | 2 |
| sword_ironbody | 1 |
| fist_straight | 3 |
| fist_sonicblow | 1 |
| bow_aimedshot | 2 |
| bow_bindingarrow | 2 |
| staff_chainlightning | 1 |
| staff_manaabsorb | 2 |

`staff_magicbolt`는 지팡이 기본기라 우선 `Unlimited`로 둔다. 마법사 감성을 위해 제한이 필요하면 별도 밸런스 Step에서 조정한다.

## Steps

### Step 0 - Design Review + ADR — 완료 (2026-07-10, Approved after design fixes)

Scope:

- 이 Task와 새 ADR 초안을 실제 코드와 대조해 리뷰한다.
- 제품 코드와 리소스는 수정하지 않는다.

Done condition:

- 설계 리뷰 판정 `Approved` 또는 `Approved after design fixes`.
- reset scope 최소 집합, runtime owner, 저장/이월 경계, 소모 순서, report/API 정책이 확정돼 있다.
- 기존 ADR-018의 "탄수 채택 안 함" 결정과 새 ammo 결정의 관계가 문서화돼 있다.

결과:

- 설계 리뷰: [[SK-002-Skill-Ammo-System-Review]] — 판정 `Approved after design fixes` (수정 반영 완료).
- ADR: [[ADR-021-Skill-Ammo-System]] — `accepted`.
- 확정 사항:
  - reset scope 최소 집합 = {Unlimited, Battle}. Expedition은 예약값(등반 lifecycle 도입 시 배선).
  - runtime owner = `CharacterArticle` 소유 plain-C# `SkillAmmoState`(StatusController 선례). Resource 미저장.
  - 저장/이월 경계 = ammo remaining은 runtime 메모리 전용. `.tres`/Resource 미직렬화. save/이월은 후속.
  - 소모 순서 = target-first, 커밋(=시전 시작/락온) 시 ammo+마나 동시 소모. 커밋 후 빗나감·스턴 취소 무환불
    (사용자 확정: 락온 후 빗나감은 의도된 전황). 무대상(시전 시작 시 사거리 내 대상 0)만 커밋 이전 무소모 Failure.
  - report/API = `SkillState.Reports` raw string 최소 기록 + 읽기 전용 `TryGetAmmo(id, out remaining, out max)`.
- ADR-018 관계: ADR-018은 ammo에 침묵(deferral)했고 "WC식 배분형 탄수 폐기"는 기획서/Skill-System 레벨
  결정이었음을 정정 문서화. ADR-021은 ADR-018의 Definition/State 분리와 정합.

### Step 1 - Runtime Ammo Gate Skeleton — 완료 (2026-07-10)

Scope:

- ammo enum/필드를 `SkillDefinition`에 추가한다.
- 유닛별 runtime ammo state를 추가한다.
- 테스트 helper로 caster에 ammo state를 주입할 수 있게 한다.
- `TurnAction_Skill`에 ammo 부족 Failure와 시전 시작 시 차감을 연결한다.
- Phase1 `.tres`는 아직 수정하지 않고 test-only definition으로 검증한다.

결과:

- `SkillAmmoResetScope` enum + `SkillDefinition.AmmoResetScope`/`Ammo` 필드 추가(`IsValidV0`에 `Ammo>=0`/enum 검증).
- `SkillAmmoState`(순수 C#, `CharacterArticle` 소유, `.tres` 미직렬화) 신규. `Charge/HasAmmo/Consume/TryGetAmmo`.
- `TurnAction_Skill.Init` 게이트를 ADR-021 §3 순서로 재정렬(ammo → 마나 가능성 → 대상 락온 → 커밋 소모).
  마나는 `CanAfford`로 확인만 하고 커밋에서 `TrySpend`. 무대상은 커밋 이전 무소모 Failure(신규 동작).
- 검증: `sk002_step1_ammo_test` 40 assertion ALL PASS. SK-001 Step1/Step3 회귀 유지(ALL PASS).
- 알려진 선행 실패(내 변경과 무관, baseline에서 동일 실패): SK-001 Step2 `A.LowestHp`, Step4 `F.HasEnoughUnits`,
  Step4b, Step5 chain, Step6 `D.ChainHitsThree`. `battle_field.tscn` 다중 상대 셋업 관련로 추정. 별도 조사 필요.

Done condition:

- `Unlimited` 스킬은 기존 SK-001 baseline과 같은 결과/RNG를 유지한다.
- 제한 스킬은 remaining ammo가 있으면 1회 차감하고 실행된다.
- remaining ammo가 0이면 HP/RNG/마나 변화 없이 `ActionState.Failure`를 반환한다.
- 마나 부족/무대상 실패는 ammo를 차감하지 않는다.
- windup 중 스턴 취소는 이미 차감된 ammo를 환불하지 않는다.
- 같은 `SkillDefinition`을 여러 유닛이 공유해도 remaining ammo가 섞이지 않는다.

### Step 2 - Phase1 Data Pack Ammo Values — 완료 (2026-07-10)

Scope:

- `Assets/SkillData/Phase1/*.tres` 12종에 ammo 필드를 입력한다.
- `Phase1SkillSpec` 정본과 `Sk001Step6DataPackTest` 서명에 ammo를 포함한다.
- 기존 SK-001 step6 데이터 검증이 ammo 드리프트도 잡게 한다.

결과:

- `Phase1SkillSpec.Def`에 `scope`/`ammo` 파라미터 추가, 12종에 수치표 초안 값 입력. `Signature`에 `scope`/`ammo`
  편입 → step6 드리프트 검증이 ammo 변화를 잡는다. 값: 기본기(sword_slash/fist_jab/bow_shot/staff_magicbolt)
  Unlimited, sword_smash 2, sword_ironbody 1, fist_straight 3, fist_sonicblow 1, bow_aimedshot 2,
  bow_bindingarrow 2, staff_chainlightning 1, staff_manaabsorb 2 (전부 Battle scope).
- 제한 스킬 8종 `.tres`에 `AmmoResetScope = 1`/`Ammo = N` 두 줄을 직접 삽입(선언 순서상 `Effects` 앞). Unlimited
  기본기 4종은 기본값 생략으로 무변경. 처음엔 `Sk001Phase1DataGenerator` 재생성을 썼으나 UID metadata churn(P3)이
  생겨, 커밋본 복원 후 최소 삽입 방식으로 재작업했다 → diff는 8파일 +16줄, `uid=` 변화 0.
- **기대 결과 변경(문서화):** Phase1 스킬이 ammo 제한을 갖게 되어 `Sk001Step6DataPackTest.RunSkill`이 실행 전
  `caster.SkillAmmoState.Charge(...)`로 ammo를 충전하도록 수정했다(Step 3 전투 reset의 테스트 대역). 충전이
  없으면 Init이 ammo fail-closed로 스킬을 실행하지 않아 실행 계열 검증(B/C)이 막힌다.
- 검증: `sk001_step6_data_pack_test` — Step 2 대역(A.Match×12, A.Roundtrip, B, C) PASS. 단 **전체 suite는 알려진
  baseline 실패 `D.ChainHitsThree`(got 1, expected 3)로 exit 1**. `sk002_step1_ammo_test`/`sk001_step1`/
  `sk001_step3` 회귀 유지. 빌드 오류 0, import exit 0.
- 선행 실패(내 변경과 무관, clean baseline에서 stash 후 rebuild로 동일 확인): step6 `D.ChainHitsThree`(got 1),
  CB-001 Step4 `D.deaths_recorded`, SK-001 Step2/4/4b/5. **근본 원인 확정(2026-07-10):** 커밋 `e5d339b "스킬 개편
  2"`가 `battle_field.tscn`의 상대를 4명(구 타입 `11_hv0hd`) → 1명(신 타입 `14_7o1qe`)으로 **의도적으로 교체**했다.
  드리프트가 아니라 스킬 개편 워크스트림의 산물이며 SK-002와 무관하다. Phase1 스킬은 어떤 씬/BT에도 배선돼 있지
  않아(grep 확인) 전투 결과에 영향을 주지 않는다. **결정(오너, 2026-07-10):** 1명 로스터는 의도된 상태이므로 씬을
  되돌리지 않고, 회귀 테스트를 self-sufficient로 rebaseline하는 별도 태스크로 처리한다. SK-002는 Step 3로 진행하되
  Step 3 검증도 self-sufficient(상대 격리/스폰·ammo 충전)로 작성한다.

Done condition:

- 12종 `.tres`가 ammo 필드를 포함해 로드/검증된다.
- 기본기는 무제한, 제한 스킬은 수치표 초안과 일치한다.
- 직렬화 round-trip에서 ammo 필드가 보존된다.
- SK-001 step1~6, CB-001 회귀가 유지되거나, Phase1 데이터 변경이 기대 결과를 바꾸는 경우 그 이유가 문서화된다.

### Step 3 - Reset Scope Integration — 완료 (2026-07-10)

결과:

- `CharacterArticle.ChargeBattleAmmo()` 추가: BT를 raw 재귀 노드 워크로 순회해 `TurnAction_Skill.Definition`을
  찾아 `SkillAmmoState.Charge(id, ammo, scope)` 호출. `TurnHelper._Ready()`의 유닛 수집 루프에서 각
  `CharacterArticle`에 호출해 **전투 시작 수명주기에 배선**. `Unlimited` no-op, `Battle`/예약 `Expedition`은
  이월 없이 full charge. remaining은 `SkillAmmoState`에만 저장(Resource 미저장).
- raw 워크 채택 이유: `FindNodeByType`는 `Root`(GetChild(0)) 하위 `TreeChildren`(일부 deferred 재구성)만 봐서
  battle-start 미갱신/Root 밖 배선에 취약. raw `GetChildren()` 재귀가 안전하고 생산 경로 결과는 동일.
- 검증: `sk002_step3_reset_test` 14 assertion ALL PASS(B가 `TurnHelper._Ready` 실제 충전을 고정). self-sufficient
  (Ally BT에만 스킬 주입 → 상대 로스터 드리프트 비의존). `sk001_step1/3`, `cb001_step1`, `sk002_step1` 회귀 유지.
  선행 baseline 실패(step6 D, cb001 step4 D)는 불변.
- reset 시점/저장 경계는 [[ADR-021-Skill-Ammo-System]] §4/§5, 본 결과에 문서화. Expedition 실이월과 save 통합은 후속.

착수 전 메모(2026-07-10):

- charge 소스는 유닛 BT의 `TurnAction_Skill` Definition 순회다(ADR-021 §4). 단 현재 `battle_field.tscn`을 포함해
  **어떤 씬/BT도 `TurnAction_Skill`/Phase1 스킬을 배선하지 않는다**(grep 확인). 즉 실제 씬에는 charge 대상이 0이라,
  battle-start reset은 test 대역 유닛(BT에 `TurnAction_Skill` 정의를 주입)으로만 관찰 가능하다. Step 1/6처럼
  self-sufficient 테스트로 검증한다.
- `battle_field.tscn` 다중 상대 회귀 실패는 SK-002와 무관한 선행 이슈이므로 Step 3 검증은 그 씬의 green baseline에
  의존하지 않게 짠다(상대 격리/스폰·ammo 명시 충전).

Scope:

- Step 0에서 확정한 reset scope를 실제 전투/등반 수명주기에 연결한다.
- 현재 등반 모델이 없으면 battle reset만 구현하고 expedition reset은 API/테스트 대역으로 남길 수 있다.
- ammo state 조회 API를 후속 HUD/리포트가 소비 가능한 형태로 둔다.

Done condition:

- reset 시작 시 제한 스킬의 remaining ammo가 `Ammo`로 충전된다.
- 전투 사이 이월이 범위에 포함된 경우, 사용한 ammo가 다음 전투에 남아 있다.
- reset 시점과 저장 경계가 문서화된다.
- ammo 상태가 `SkillDefinition` Resource에 저장되지 않는다.

### Step 4 - Documentation and Review — 완료 (2026-07-10)

Scope:

- [[Skill-System]], [[Current-State]], [[Open-Tasks]], 기획서 관련 절을 현재 사실로 갱신한다.
- 완료 리뷰를 작성한다.

Done condition:

- ammo 데이터 모델, runtime owner, 소모/환불 정책, reset scope, 검증 결과가 문서화된다.
- P0/P1 리뷰 문제가 없다.

결과:

- [[Skill-System]] "Ammo(SK-002, ADR-021)" 절 신규(기존 "Ammo Follow-up" 대체): 데이터 모델·runtime owner·
  소모/환불 정책·reset scope·battle-start 배선·Phase1 수치·report·후속. Model/현재 상태/Verification 절도 ammo
  반영. [[Current-State]] Combat에 SK-002 완료 엔트리 + Known Regressions 배너 근본 원인 정정. [[Open-Tasks]]
  SK-002 완료 처리 + "무대상/환불 정책 확정" 후속 해소 표시 + rebaseline Task 등록.
- 완료 리뷰: [[SK-002-Skill-Ammo-System-Review]] Step 0~3 각 처리결과/재판정(전부 완료). Step 4 문서 갱신으로
  전체 SK-002 완료. P0/P1 없음.
- 검증(누적): `sk002_step1_ammo_test`(40)·`sk002_step3_reset_test`(14) ALL PASS, `sk001_step1/3`·`cb001_step1`·
  `sk001_step6` A/B/C 회귀 유지, 빌드 경고/오류 0, `--import` exit 0. 선행 baseline 실패(step6 D, cb001 step4 D)는
  `battle_field.tscn` 개편 소관으로 SK-002 무관.

## Verification Plan

- C# headless tests:
  - ammo 부족 Failure.
  - ammo 차감 성공.
  - 마나 부족/무대상 실패에서 ammo 미차감.
  - windup 스턴 취소에서 ammo 미환불.
  - shared `SkillDefinition` + per-unit ammo isolation.
  - reset scope 충전/이월.
  - Phase1 `.tres` ammo serialization and signature drift.
- Existing regression:
  - SK-001 step1~6.
  - ST-001 mana wiring tests if mana/ammo interaction changes.
  - CB-001 step1~4 deterministic regression.
- Godot headless editor load/import:
  - 새 enum/필드 직렬화와 `.tres` parse 확인.

## Risks

- reset scope를 등반 단위로 먼저 구현하려면 아직 완성되지 않은 outgame/expedition lifecycle과 결합할 수 있다. Step 1은 test-owned runtime state로 시작하는 편이 안전하다.
- `TurnAction_Skill.Init`은 현재 마나 지불을 대상 확정보다 먼저 수행한다. ammo 정책은 대상 없음에서 소모하지 않는 쪽을 권장하므로, 마나 지불 순서도 함께 재검토해야 한다.
- ammo와 마나를 모두 요구하면 강기가 과도하게 막힐 수 있다. Phase1 수치는 기본기 무제한과 제한 스킬 ammo를 넉넉히 두는 방향으로 시작한다.
- 기존 `SkillState.Reports`는 raw string 기반이다. ammo 부족/소모 report를 추가하면 GL-001 adapter 어휘와 함께 정리해야 한다.
- `SkillDefinition` 필드 추가는 기존 `.tres` 호환성을 확인해야 한다. 기본값은 기존 리소스가 무제한처럼 동작해야 한다.

## Related

- [[Skill-System]]
- [[SK-001-Data-Driven-Skill-System]]
- [[ADR-018-Data-Driven-Skill-System]]
- [[Turn-System]]
- [[BehaviorTree-System]]