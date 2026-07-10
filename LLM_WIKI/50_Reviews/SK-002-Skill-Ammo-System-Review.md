---
type: review
task: SK-002
step: 0-4
status: complete
reviewed: 2026-07-10
---

# SK-002 Skill Ammo System — Step 0 설계 리뷰

SK-002 Step 0 설계 리뷰다. 이번 세션은 제품 코드와 리소스를 수정하지 않고, Task
[[SK-002-Skill-Ammo-System]], 새 ADR 초안 [[ADR-021-Skill-Ammo-System]], 시스템 문서
[[Skill-System]], 그리고 실제 스킬/전투/BT 코드를 대조했다.

대조한 실제 코드:

- `Assets/Script/SkillSystem/TurnAction_Skill.cs` (실행기, Init/Action/SelectTarget)
- `Assets/Script/SkillSystem/SkillDefinition.cs` (불변 Resource, IsValidV0)
- `Assets/Script/SkillSystem/SkillState.cs` (유닛별 실행 상태, Reports)
- `Assets/Script/Article/CharacterArticle.cs` (CurrentTurnActionState/StatusController owner, TurnPlay)
- `Assets/Script/Article/Status/Element/Mana.cs` (TrySpend)
- `Assets/Script/Tests/Sk001Step1SkillSystemTest.cs` (헬퍼 주입/격리 테스트 패턴)

## 요약 판정

**Approved after design fixes.** 아키텍처 방향(불변 Definition + 유닛별 runtime state + 실행 게이트
Failure)은 기존 코드 구조와 정확히 맞물린다. runtime owner에는 `StatusController`라는 강한 선례가 있고,
Definition 필드 추가는 backward-compatible additive다. 다만 아래 **설계 수정 3건**을 Step 1 착수 전에 Task/ADR에
확정해 반영해야 한다. 세 건 모두 관찰 가능한 동작이나 baseline 경계에 직접 영향을 준다.

## Findings

### 1. [P1] 소모 순서 재정렬은 기존 마나 게이트의 관찰 동작을 바꾼다 — 명시 필요

- 현재 `TurnAction_Skill.Init`은 **대상 확정 이전에** 마나를 지불한다
  (`Assets/Script/SkillSystem/TurnAction_Skill.cs:30`에서 `TrySpendMana`, 대상 선택은 `:37`~`:44`).
  또한 대상이 없어도(`target == null`) fail-closed하지 않고 그대로 windup/effect phase를 진행해 **턴과 마나를
  소모하는 no-op**이 된다.
- Task의 Consumption Policy(§Proposed Architecture)는 반대로 `대상 확정 → 커밋 시 ammo+마나 동시 소모`,
  `무대상이면 소모 없이 Failure`를 요구한다. 이는 ammo 도입뿐 아니라 **마나 지불 시점 재정렬 + 무대상 fail-closed**
  라는 legacy 마나 경로의 동작 변경을 동반한다.
- ADR-018 Follow-up(`ADR-018-Data-Driven-Skill-System.md:165`)이 "무대상 fail-closed/마나 환불 여부"를 이미
  미결로 남겨 뒀으므로, 이는 회귀가 아니라 **승인된 결정 지점**이다. 다만 결정을 문서에 못 박아야 한다.
- **baseline 영향 분석(중요):** Phase1 기본기는 전부 `ManaCost == 0` + `Unlimited`이고, SK-001 baseline
  테스트는 항상 인접 유효 대상을 세팅한다(`Sk001Step1SkillSystemTest.cs:271` `PrepareAdjacentTarget`).
  따라서 "유효 대상 존재 + ManaCost 0/ammo 무제한" 경로에서는 재정렬이 **no-op**이라 RNG/HP baseline이 보존된다.
  유일한 실질 변화는 "마나 스킬을 사거리 내 대상 없이 발동" 케이스가 기존 마나 소모 no-op → **소모 없는 Failure(BT
  fallback)**로 바뀌는 것이다.
- **설계 수정:** ADR-021에 최종 소모 순서를 고정한다(권장: Open Decision 1의 target-first). Step 1 완료 조건에
  "무대상 Failure는 신규 동작이며 SK-001 baseline은 유효 대상 fixture라 보존됨"을 명시한다.

### 2. [P2] runtime owner를 `CharacterArticle` 소유 plain-C# 상태로 확정 — Resource 저장 금지 계약

- Task는 owner 후보를 `SkillAmmoState` / `CharacterArticle` / `SkillLoadoutRuntime`로 열어 뒀다.
- 실제 코드에 정확한 선례가 있다: `CharacterArticle.StatusController { get; } = new();`
  (`Assets/Script/Article/CharacterArticle.cs:18`)는 Godot Resource가 아닌 순수 C# 객체를 유닛별로 소유하며,
  `_Ready`/턴 수명주기에서 갱신되고 `.tres`에 직렬화되지 않는다. `CurrentTurnActionState`(`:17`)도 같은 성격이다.
- **설계 수정:** ammo runtime을 `StatusController`와 동형의 plain-C# 객체(`SkillAmmoState`)로 두고
  `CharacterArticle`이 소유하도록 ADR에 고정한다. `SkillDefinition.Id`로 키잉하며, `SkillDefinition` Resource에는
  절대 remaining을 쓰지 않는다(Step 3 done condition의 "Resource에 저장되지 않는다"를 owner 계약으로 보장).

### 3. [P2] reset scope 최소 집합 = {Unlimited, Battle}, Expedition은 예약 — 미배선 값의 안전 처리

- Task Proposed Architecture는 `Unlimited/Battle/Expedition` 3값을 제안하나, 현재 코드에는 등반/출전
  (expedition) 수명주기가 없다(Task Risks도 명시). 미구현 lifecycle에 지금 결합하면 Step 1이 outgame으로 번진다.
- 미배선 enum 값을 `IsValidV0`이 통과시키되 런타임이 조용히 오작동하면 데이터 함정이 된다.
- **설계 수정:** Step 1~3 배선 대상은 `Unlimited` + `Battle`로 한정한다. `Expedition`은 enum에 예약값으로만 두고
  (ADR-018의 `combo` 예약 필드 선례와 동일), Phase1 `.tres`는 사용하지 않으며, 런타임은 `Expedition`을 만나면
  이월 없는 `Battle`로 취급하고 그 divergence를 문서화한다. Step 3에서 expedition lifecycle이 생기면 실제 이월을
  붙인다.

### 4. [P3] "ADR-018의 탄수 채택 안 함 결정"이라는 Step 0 done-condition 표현은 부정확

- Task Step 0 done condition은 "기존 ADR-018의 '탄수 채택 안 함' 결정과 새 ammo 결정의 관계"를 문서화하라고 한다.
- 그러나 `ADR-018-Data-Driven-Skill-System.md` 본문에는 탄수/ammo 언급이 전혀 없다. ADR-018은 ammo에 대해
  **침묵(deferral)**했다. "WC식 배분형 탄수 채택 안 함"은 기획서/`[[Skill-System]]`의 Ammo Follow-up 절
  (`Skill-System.md:58`~`64`)과 2026-07-09 기획 정정 레벨의 결정이다.
- **설계 수정:** ADR-021은 "ADR-018 결정을 뒤집는다"가 아니라 "ADR-018이 미룬 ammo를 결정하고, ADR-018의
  Definition/State 분리와 정합한다"로 정확히 기술한다. 이는 [[ADR-021-Skill-Ammo-System]] §Context에 반영했다.

### 5. [P3] Phase1 데이터 드리프트 가드 지점 확인

- Step 2는 `Phase1SkillSpec` 정본과 `Sk001Step6DataPackTest` 서명에 ammo를 포함하라고 한다. 실제로 그 두 파일이
  존재하며(`Assets/Script/Tests/Phase1SkillSpec.cs`, `Sk001Step6DataPackTest.cs`) 서명 기반 드리프트 검증
  구조라 ammo 필드 추가가 자연스럽게 편입 가능하다. 구조적 문제 없음. Step 2 착수 시 서명에 ammo/scope 두 필드를
  모두 포함할 것.

## Open Decisions

### OD-1. 소모 순서와 무대상 정책 — **확정(사용자 승인, 2026-07-10)**

확정안(target-first, 커밋=시전 시작, 커밋 후 무환불):

- **전제조건(게이트 아님):** definition/caster 유효성 실패는 저작/구조 오류이므로 상태 없이 반환 → 기존 no-op
  `End` 유지(SK-001 계약). caster 조회 실패는 상태를 붙일 caster가 없어 상태 기반 Failure 자체가 불가능하다.
  아래 런타임 게이트만 무소모 Failure를 낸다. (Step 1 코드 리뷰 P2 처리로 확정, ADR-021 §3.)
1. ammo 제한 스킬이면 `remaining > 0` → 부족 시 무소모 Failure
2. 마나 지불 가능성(`CanAfford`) → 부족 시 무소모 Failure
3. 대상 락온(Self는 항상 caster) → **시전 시작 시점**에 사거리 내 유효 대상이 하나도 없으면 무소모 Failure
4. 커밋(시전 시작 = 대상 락온) 시 ammo 1 + 마나 동시 소모
5. 커밋 이후 환불 없음 — 여러 턴 windup 중 락온 대상이 이동/사망해 빗나가도, 스턴 취소돼도 환불하지 않는다
   (`CancelCasting`의 기존 "미환불" 계약과 일치, `CharacterArticle.cs:26`~`:31`)

**사용자 확정 의도:** 소모 기준점은 착탄이 아니라 시전 시작(대상 락온)이다. 장전형 스킬이 락온한 위치로 발사했으나
그 사이 적이 빠져나가 빗나가는 것은 **의도된 전황의 일부**이므로, 커밋된 ammo/마나는 결과와 무관하게 되돌리지
않는다. "발사 전에 조준할 대상조차 없는" 경우(§4 무대상)만 커밋 이전 게이트로 걸러 무소모 Failure를 낸다.

이 한 건만 기존 관찰 동작을 바꾼다(§Finding 1): "시전 시작 시점에 사거리 내 대상이 아예 없는 마나 스킬 발동"이
기존 마나 소모 no-op → 무소모 Failure(BT fallback)로 바뀐다. ADR-021 §3에 이 의미로 고정했다.

### OD-2. report 최소 정책 (권장: raw string, GL-001 이관은 후속)

`SkillState.Reports`(`List<string>`)에 소모 시 1줄, 부족 실패 시 1줄만 append한다. 구조화 어휘/`GL-001` 어댑터
정리는 Task Risk(line 233)대로 후속. Step 1은 검증용 raw string으로 충분.

### OD-3. HUD 상태 조회 API (권장: 읽기 전용 조회, 소유자에 배치)

Out of Scope는 HUD를 제외하되 "후속 UI가 읽을 수 있는 상태 API"는 허용한다(`SK-002:61`). 권장: ammo owner에
`bool TryGetAmmo(StringName skillId, out int remaining, out int max)`를 두고 Unlimited/미보유는 false 반환.
읽기 전용, 변이 없음. HUD 구현은 안 하되 읽을 창구만 확보.

## Step Assessment

- **Step 0 (본 리뷰):** done condition 충족 가능. reset scope 최소 집합/runtime owner/저장 경계/소모 순서/
  report·API 정책을 위 Findings·Open Decisions로 확정했고, ADR-018 관계를 §Finding 4로 정정 문서화했다.
- **Step 1 (Runtime Ammo Gate Skeleton):** 입력=승인된 ADR-021, 출력=enum/필드 + `SkillAmmoState` + 테스트
  주입 + `TurnAction_Skill` 게이트. 선행 조건=OD-1 확정. 범위 적절. 단, "무대상 Failure"가 신규 동작임을 완료
  조건에 명시할 것(§Finding 1). test-only definition으로 검증하고 `.tres` 미수정은 타당.
- **Step 2 (Phase1 Data Pack):** 입력=Step 1 필드, 출력=12종 `.tres` + 서명 갱신. 구조적 문제 없음(§Finding 5).
  기본기 Unlimited / 제한 스킬 수치표는 밸런스 초안이므로 Step 2에서 확정.
- **Step 3 (Reset Scope Integration):** Battle reset만 구현, Expedition은 API/테스트 대역(§Finding 3과 일치).
  charge 소스=유닛 BT의 `TurnAction_Skill` Definition 순회(기존 `AttackRangePositions`가 이미 BT를 순회,
  `CharacterArticle.cs:42`)를 loadout 대역으로 사용 권장. Step 1은 헬퍼 직접 주입으로 우회 가능.
- **Step 4 (Documentation and Review):** 표준.

Step 병합/분할 불필요. 4단계 경계가 각각 한 구조 경계를 다룬다.

## Verification Assessment

Task Verification Plan은 충분하되 아래 실패 시나리오를 추가/강조한다.

- **무대상 무소모 Failure:** 마나 스킬을 사거리 밖에서 발동 → HP/RNG/마나 불변 + `ActionState.Failure`
  (§Finding 1의 신규 동작을 직접 고정하는 테스트. 기존 계획엔 "무대상 미차감"만 있고 마나 미차감/ Failure 반환이
  약함).
- **Unlimited baseline 보존:** 같은 seed에서 SK-001 Step1 슬래시 baseline 문자열(상태열|hp|next_rng|anim)이
  ammo 도입 전후 완전 일치(`Sk001Step1SkillSystemTest.cs:119` 패턴 재사용).
- **shared Definition + per-unit ammo 격리:** `Sk001Step1SkillSystemTest.cs:159`의 격리 테스트 패턴을 ammo
  remaining에 적용 — 한 유닛의 차감이 다른 유닛 remaining을 건드리지 않음.
- **스턴 취소 미환불:** windup 중 `ConsumeStunForTurn`(`CharacterArticle.cs:65`) 발동 후 remaining이 차감된
  상태 유지.
- **Expedition 미배선 안전:** `AmmoResetScope.Expedition` `.tres`가 로드되고 런타임이 이월 없는 Battle로
  안전 처리(§Finding 3).
- **serialization round-trip:** 두 신규 필드(`AmmoResetScope`, `Ammo`)가 `.tres` 저장/재로드에서 보존
  (`Sk001Step1SkillSystemTest.cs:107` round-trip 패턴 재사용).

빠진 것 없음. 회귀 축(SK-001 step1~6, CB-001, ST-001 마나)은 Task에 이미 열거됨.

## Verdict

**Approved after design fixes.**

Step 1 착수 전 반영 필요 (3건 모두 [[ADR-021-Skill-Ammo-System]]에 확정 반영 완료):

1. OD-1(소모 순서/무대상 정책) — **사용자 승인 완료(2026-07-10)**. 커밋=시전 시작, 락온 후 빗나감·취소 무환불,
   무대상만 커밋 이전 무소모 Failure. ADR-021 §3에 고정.
2. runtime owner를 `CharacterArticle` 소유 plain-C# `SkillAmmoState`로 고정(§Finding 2, ADR-021 §2).
3. reset scope 최소 집합 {Unlimited, Battle} + Expedition 예약을 고정(§Finding 3, ADR-021 §4).

Step 0 done condition(reset scope 최소 집합 / runtime owner / 저장·이월 경계 / 소모 순서 / report·API 정책
확정 + ADR-018 관계 문서화)이 모두 충족됐다. Step 1 구현 프롬프트로 진행 가능하다.

## Related

- [[SK-002-Skill-Ammo-System]]
- [[ADR-021-Skill-Ammo-System]]
- [[ADR-018-Data-Driven-Skill-System]]
- [[Skill-System]]
- [[SK-001-Data-Driven-Skill-System-Review]]

---

# SK-002 Skill Ammo System — Step 1 코드 리뷰

리뷰 일자: 2026-07-10

대조 대상:

- [[SK-002-Skill-Ammo-System]] Step 1 완료 조건
- [[ADR-021-Skill-Ammo-System]]
- `Assets/Script/SkillSystem/SkillDefinition.cs`
- `Assets/Script/SkillSystem/SkillAmmoState.cs`
- `Assets/Script/SkillSystem/TurnAction_Skill.cs`
- `Assets/Script/Article/CharacterArticle.cs`
- `Assets/Script/Tests/Sk002Step1AmmoTest.cs`

## 발견 사항

1. **[P2] ADR-021의 invalid definition/caster Failure 정책이 코드에 반영되지 않았다**
   - ADR-021 §3은 게이트 첫 단계로 "definition/caster 유효성 → 실패 시 무소모 Failure"를 확정한다.
   - 현재 `TurnAction_Skill.Init`은 caster 조회 실패, null definition, invalid definition에서 그냥 `return`한다. 이후 `Action`은 `CurrentTurnActionState`가 없으므로 `ActionState.End`를 반환한다.
   - BT 경로에서는 `End`가 `BtStatus.Success`로 매핑되므로, invalid skill row가 fallback을 막는 기존 no-op Success 동작이 유지된다.
   - 이는 SK-001의 기존 invalid definition 정책(`no state + End`)과는 일치하지만, "승인된 ADR-021대로 구현"이라는 Step 1 기준과는 어긋난다.
   - 권장 수정 방향: 둘 중 하나를 선택한다. (A) ADR-021에 맞춰 invalid definition/caster도 `Failed` state를 만들어 `Failure`로 닫고 SK-001 invalid test 기대값을 갱신한다. (B) 기존 SK-001 invalid 정책을 유지하기로 결정한다면 ADR-021 §3과 SK-002 Task에 "ammo/mana/target gate 실패만 Failure, invalid definition은 기존 no-op End 유지"라고 명시한다.
   - 위치: `Assets/Script/SkillSystem/TurnAction_Skill.cs:20`~`:24`, `:105`~`:114`; BT 매핑은 `Assets/Script/AutoCrawlerBehaviorTree/Action/BehaviorTree_TurnAction.cs:23`~`:35`.

## 검증 결과

- `dotnet build AutoCrawler.sln -c Debug` — PASS, warnings 0, errors 0.
- `godot --headless --path . --import` — exit 0. 기존 `graph_offset deprecated`, ObjectDB/resource leak 종료 로그만 출력.
- `sk002_step1_ammo_test.tscn` — ALL PASS(40 assertions).
- `sk001_step1_skill_system_test.tscn` — ALL PASS.
- `sk001_step3_mana_hit_test.tscn` — ALL PASS.

## 완료 조건 대조

- `Unlimited` 스킬 baseline 보존: SK-002 B + SK-001 Step1 baseline PASS.
- 제한 스킬 차감 후 실행: SK-002 C PASS.
- remaining 0 Failure + HP/RNG/마나 불변: SK-002 D PASS.
- 마나 부족/무대상 실패 ammo 미차감: SK-002 E/F PASS.
- windup 스턴 취소 ammo 미환불: SK-002 G PASS.
- shared definition per-unit ammo isolation: SK-002 H PASS.

## 판정

**수정 필요(P2 1건, P0/P1 없음).** → **처리 완료 후 완료(2026-07-10).**

Step 1의 ammo 핵심 동작과 회귀 검증은 통과했다. 다만 ADR-021이 명시한 invalid definition/caster Failure 정책과 실제 코드의 기존 no-op End 정책이 충돌한다. 이 충돌을 코드 수정 또는 ADR/Task 문서 수정으로 해소하면 Step 1은 완료 판정 가능하다.

## 처리 결과

### [P2] invalid definition/caster Failure 정책 충돌 — 문서 수정(Option B)

- **결정:** 코드가 아니라 ADR/리뷰 문서를 수정해 계약을 정합화했다. invalid definition/caster는 런타임 전술
  실패가 아니라 저작/구조 오류이므로 기존 no-op `End`를 유지하고, ammo/mana/target 런타임 게이트만 `Failure`를
  낸다.
- **결정 근거(코드 수정이 부적절한 이유):**
  1. `Failure`는 `caster.CurrentTurnActionState.Failed` 채널로만 전달된다. `TryGetCaster` 실패 시 상태를 붙일
     caster가 없어 caster-invalid는 상태 기반 `Failure`를 **구조적으로 낼 수 없다**. definition-invalid만 Failure로
     바꾸면 두 유효성 실패가 서로 다른 BT 결과(Failure vs End)를 내는 비일관 계약이 된다.
  2. `IsValidV0` 실패(잘못된 Id/음수 Range/미정의 enum/null 블록)는 데이터/배선 버그다. BT fallback으로 삼키면
     오류를 조용히 감춰 디버깅을 어렵게 한다.
  3. SK-001 invalid test(`Sk001Step1SkillSystemTest.cs` D: `no state + End`) 계약을 보존해 SK-002 범위를 ammo에
     한정한다.
- **변경 문서:** [[ADR-021-Skill-Ammo-System]] §3 — 유효성을 "런타임 Failure 게이트"에서 분리해 **진입
  전제조건**으로 명시하고, 게이트 번호를 ammo/mana/target 3종으로 재정리. 본 리뷰 OD-1 확정안도 동일하게 갱신.
- **코드 무변경 확인:** `TurnAction_Skill.Init`의 유효성 early-return은 그대로 두며, 재검증 불필요(코드 미변경).
  기존 검증(빌드/import/sk002·sk001 테스트 PASS)이 그대로 유효하다.

## 재판정

**완료.** P2가 문서 정합화로 해소됐고 코드 변경이 없어 회귀 위험이 없다. Step 1 완료 조건 6개는 모두 충족되며
(SK-002 A~H, SK-001 Step1/Step3 PASS), invalid definition/caster 정책은 ADR-021과 코드가 이제 일치한다.
Step 2(Phase1 데이터 팩)로 진행 가능하다.
---

# SK-002 Skill Ammo System — Step 2 코드 리뷰

리뷰 일자: 2026-07-10

대조 대상:

- [[SK-002-Skill-Ammo-System]] Step 2 완료 조건
- [[ADR-021-Skill-Ammo-System]]
- `Assets/Script/Tests/Phase1SkillSpec.cs`
- `Assets/Script/Tests/Sk001Step6DataPackTest.cs`
- `Assets/SkillData/Phase1/*.tres`

## 발견 사항

1. **[P2] `sk001_step6_data_pack_test` 전체 suite를 ALL PASS로 보고하면 실제 exit code와 불일치한다**
   - 직접 실행 결과 `sk001_step6_data_pack_test.tscn`은 `D.ChainHitsThree -> got 1, expected 3`에서 실패하고
     프로세스가 exit 1로 끝난다.
   - ammo 관련 Step 2 범위인 데이터 드리프트 검증(A.Match 12종/Roundtrip)과 실행 계열(B/C)은 PASS이므로,
     구현 자체의 blocker로 보지는 않는다. 또한 Task 문서가 `D.ChainHitsThree`를 선행 실패로 분리해 둔 점은 타당하다.
   - 다만 완료 보고/Task 결과에서 `sk001_step6_data_pack_test` 자체가 ALL PASS인 것처럼 읽히면 재현 결과와
     충돌한다. 정확한 표현은 "Step 2 관련 A/B/C 대역 PASS, 전체 suite는 알려진 baseline D 실패로 exit 1"이다.
   - 위치: `Assets/Script/Tests/Sk001Step6DataPackTest.cs:220`.

2. **[P3] Phase1 `.tres` 재생성 과정에서 resource/ext_resource UID metadata가 제거됐다**
   - 예: `staff_chainlightning.tres`, `sword_ironbody.tres`, `sword_slash.tres`에서 `[gd_resource ... uid="..."]`와
     script `ext_resource uid="..."`가 빠졌다.
   - 현재 검색상 Phase1 `.tres` resource UID를 참조하는 생산 씬/BT는 없고, Godot import도 통과하므로 Step 2
     blocker는 아니다.
   - 다만 UID churn은 향후 외부 참조가 생긴 뒤에는 불필요한 리스크가 될 수 있으므로, 생성기 또는 저장 절차에서
     가능하면 기존 UID metadata를 보존하는 편이 좋다.

## 긍정 확인

- `Phase1SkillSpec.Def`가 `scope`/`ammo`를 받아 `SkillDefinition.AmmoResetScope`/`Ammo`에 기록한다
  (`Assets/Script/Tests/Phase1SkillSpec.cs:17`~`:28`).
- 12종 수치표 값이 의도와 일치한다. 기본기 4종은 `Unlimited/0`, 제한 스킬 8종은 `Battle` scope와 고정 ammo를 가진다
  (`Assets/Script/Tests/Phase1SkillSpec.cs:33`~`:63`).
- `Signature`에 `scope`/`ammo`가 포함되어 데이터 드리프트 검증이 ammo 변경도 잡는다
  (`Assets/Script/Tests/Phase1SkillSpec.cs:83`~`:86`).
- `Sk001Step6DataPackTest.RunSkill`이 실행 전 `SkillAmmoState.Charge(...)`를 호출해 Step 3 reset 미구현 구간을
  테스트 대역으로 메운다(`Assets/Script/Tests/Sk001Step6DataPackTest.cs:238`~`:244`).
- Phase1 데이터 리소스는 제한 스킬에 `AmmoResetScope = 1`/`Ammo = N`을 저장하고, Unlimited 기본기는 기본값 생략으로
  기존 리소스 호환성을 유지한다.

## 검증 결과

- `dotnet build AutoCrawler.sln -c Debug` — PASS, warnings 0, errors 0.
- `godot --headless --path . --import` — exit 0. 기존 `graph_offset deprecated`, ObjectDB/resource leak 종료 로그만 출력.
- `sk001_step6_data_pack_test.tscn` — Step 2 관련 A/B/C 대역 PASS. 전체 suite는 알려진 선행 실패
  `D.ChainHitsThree -> got 1, expected 3`로 exit 1.
- `sk002_step1_ammo_test.tscn` — ALL PASS(40 assertions).
- `sk001_step1_skill_system_test.tscn` — ALL PASS.
- `sk001_step3_mana_hit_test.tscn` — ALL PASS.

## 완료 조건 대조

- 12종 `.tres` ammo 필드 로드/검증: PASS(A.Match 12종, Roundtrip).
- 기본기 무제한/제한 스킬 수치표 일치: PASS.
- 직렬화 round-trip ammo 보존: PASS.
- SK-001/CB-001 회귀 유지 또는 기대 결과 변경 문서화: 부분 PASS. Step 2 관련 회귀는 유지되며, 기존
  `battle_field.tscn` 다중 상대 셋업 드리프트는 Task 문서에 별도 선행 실패로 문서화됐다. 다만 전체 Step6 suite의
  exit 1은 완료 보고에서 더 정확히 표현해야 한다.

## 판정

**수정 필요(P2 1건, P0/P1 없음).** → **처리 완료 후 완료(2026-07-10).**

Step 2 구현 자체는 SK-002의 데이터 팩 목표를 충족한다. `Phase1SkillSpec`/`.tres`/서명/테스트 대역의 방향은 모두
적절하고, ammo 관련 검증도 통과했다. 남은 P2는 코드 결함이 아니라 검증 보고 정확도 문제다. Task/완료 보고에서
`sk001_step6_data_pack_test` 전체를 ALL PASS로 표현하지 않고, "A/B/C PASS + 알려진 D baseline 실패로 suite exit 1"로
정정하면 Step 2는 완료 판정 가능하다. P3 UID metadata churn은 후속 정리 권고이며 blocker가 아니다.

## 처리 결과

### [P2] `sk001_step6_data_pack_test` 보고 정확도 — 표현 정정

- Task 결과와 완료 보고에서 `sk001_step6_data_pack_test`를 "ALL PASS"로 쓰지 않고 **"Step 2 대역(A.Match×12,
  A.Roundtrip, B, C) PASS. 단 전체 suite는 알려진 baseline 실패 `D.ChainHitsThree`(got 1, expected 3)로
  exit 1"** 로 정정했다. `D.ChainHitsThree`는 clean baseline에서도 동일하게 실패하는 `battle_field.tscn` 다중
  상대 드리프트로, 별도 스핀오프 태스크가 커버한다.

### [P3] Phase1 `.tres` UID metadata churn — UID 보존 방식으로 재작업

- **원인:** `Sk001Phase1DataGenerator`(ResourceSaver.Save)는 인메모리 `SkillDefinition`을 새로 저장하므로
  기존 파일의 `[gd_resource]`/`[ext_resource]` `uid="uid://..."`를 잃는다. 12종 전체(무변경이어야 할 Unlimited 4종
  포함)에 UID churn이 발생했다.
- **수정:** 생성기 재생성을 되돌리고(`git checkout -- Assets/SkillData/Phase1`), 제한 스킬 8종의 `.tres`에만
  `AmmoResetScope = 1` / `Ammo = N` 두 줄을 `Effects` 앞(선언 순서)에 직접 삽입했다. Unlimited 4종은 무변경.
- **결과 diff:** 8개 파일 +16줄만, `uid=` 라인 추가/삭제 0. 각 파일의 커밋된 헤더/UID 상태를 그대로 보존한다.
  (참고: 커밋된 데이터 자체가 일부는 UID 보유, 일부는 미보유로 이미 혼재해 있었다. 이번 수정은 그 상태를 건드리지
  않는다.)
- **재검증:** `--import` exit 0, `sk001_step6` A.Match×12/A.Roundtrip/B/C PASS(값 로드 정상), `sk002_step1`/
  `sk001_step1`/`sk001_step3` PASS.

## 재판정

**완료.** P2는 보고 표현 정정으로, P3는 UID 보존 재작업으로 해소됐다. Step 2 완료 조건(12종 ammo 로드/검증, 기본기
무제한·제한 수치표 일치, round-trip 보존, 회귀 유지/기대 결과 변경 문서화)이 충족된다. `D.ChainHitsThree` 및
CB-001 Step4 `D.deaths_recorded`는 SK-002와 무관한 선행 `battle_field.tscn` 드리프트로 별도 태스크가 담당한다.

## 처리 결과

### [P2] `sk001_step6_data_pack_test` 전체 suite ALL PASS 표현 — 문서 정정 완료

- Task Step 2 결과가 `sk001_step6_data_pack_test` 전체 PASS가 아니라, **Step 2 대역(A.Match×12, A.Roundtrip, B, C) PASS + 알려진 baseline 실패 `D.ChainHitsThree`로 전체 suite exit 1**이라고 정정됐다.
- 직접 재실행 결과도 문서와 일치한다. A/B/C는 모두 PASS이고, D만 `got 1, expected 3`로 실패한다.
- 따라서 이전 P2는 해소됐다.

### [P3] `.tres` UID metadata churn — 최소 diff로 재작업 완료

- 재생성으로 제거됐던 resource/ext_resource `uid=` metadata가 복원됐다.
- 현재 Phase1 `.tres` diff는 제한 스킬 8종에 `AmmoResetScope = 1` / `Ammo = N` 두 줄을 추가하는 최소 변경으로 정리됐다.
- Unlimited 기본기 4종은 기본값 생략으로 유지된다.

## 재검증 결과

- `dotnet build AutoCrawler.sln -c Debug` — PASS, warnings 0, errors 0.
- `sk001_step6_data_pack_test.tscn` — Step 2 관련 A/B/C PASS. 전체 suite는 알려진 baseline 실패 `D.ChainHitsThree -> got 1, expected 3`로 exit 1.
- `sk002_step1_ammo_test.tscn` — ALL PASS(40 assertions).
- `sk001_step1_skill_system_test.tscn` — ALL PASS.
- `sk001_step3_mana_hit_test.tscn` — ALL PASS.

## 재판정

**완료.** Step 2의 P2/P3 지적은 모두 반영됐다. 남은 `sk001_step6` D 실패는 ammo 데이터 팩 변경과 무관한 기존 baseline 실패로 문서화되어 있으며, Step 2 완료 조건을 막지 않는다. Step 3(Reset Scope Integration)로 진행 가능하다.

---

# SK-002 Skill Ammo System — Step 3 코드 리뷰

리뷰 일자: 2026-07-10

대조 대상:

- [[SK-002-Skill-Ammo-System]] Step 3 완료 조건
- [[ADR-021-Skill-Ammo-System]] §4 reset scope
- `Assets/Script/TurnHelper.cs`
- `Assets/Script/Article/CharacterArticle.cs`
- `Assets/Script/SkillSystem/SkillAmmoState.cs`
- `Assets/Script/AutoCrawlerBehaviorTree/Action/BehaviorTree_TurnAction.cs`

## 발견 사항

1. **[P1] 전투 시작 reset이 실제 수명주기에 연결되지 않았다**
   - Step 3 완료 조건은 "reset 시작 시 제한 스킬의 remaining ammo가 `Ammo`로 충전된다"이다.
   - 현재 `SkillAmmoState.Charge(...)` 호출자는 Step 1 테스트와 `Sk001Step6DataPackTest.RunSkill` 테스트 대역뿐이다.
     `TurnHelper._Ready()`는 RNG 초기화와 turn list 수집 후 `AdvanceToNextTurn()`만 호출하고 ammo 충전이 없다
     (`Assets/Script/TurnHelper.cs:29`~`:55`). `AdvanceToNextTurn()`도 turn-start status만 적용한다
     (`Assets/Script/TurnHelper.cs:110`~`:115`).
   - `CharacterArticle.ApplyTurnStartEffects()` 역시 `StatusController`/turn-start status/legacy affecting status만 처리하고 ammo reset을
     하지 않는다(`Assets/Script/Article/CharacterArticle.cs:56`~`:61`).
   - 따라서 실제 전투 시작 시 BT에 제한 `TurnAction_Skill`이 배선되어 있어도 remaining이 등록되지 않아, 첫 시전이
     `TurnAction_Skill.Init`의 ammo gate에서 fail-closed된다.
   - 권장 수정: ADR-021 §4대로 battle-start reset 경계에서 각 `CharacterArticle`의 BT를 순회해
     `BehaviorTree_TurnAction.TurnAction is TurnAction_Skill { Definition: { ... } }`인 항목을 찾아
     `character.SkillAmmoState.Charge(def.Id, def.Ammo, def.AmmoResetScope)`를 호출한다. 현재 등반 lifecycle은 없으므로
     `Battle`과 예약 `Expedition`은 둘 다 이월 없는 battle charge로 처리하고, `Unlimited`는 `Charge`가 no-op인 현재 계약을 재사용한다.

2. **[P2] Step 3 self-sufficient 검증 자산이 없다**
   - 현재 untracked/changed 파일 중 `sk002_step3_*` 테스트가 없고, Task 문서의 Step 3도 "착수 전 메모" 상태로 남아 있다.
   - Step 3은 기존 씬에 `TurnAction_Skill` 배선이 없다는 전제를 이미 문서화했으므로, 테스트 대역 BT에 제한/무제한/Expedition
     스킬을 주입해 battle reset을 관찰하는 self-sufficient 테스트가 필요하다.
   - 최소 검증: (A) battle reset 후 제한 스킬 remaining=max, (B) 같은 Definition을 여러 유닛이 써도 per-unit 격리,
     (C) Unlimited는 entry 없음, (D) Expedition 예약값은 battle charge처럼 충전, (E) reset 후 1회 사용하면 remaining 감소하되
     `SkillDefinition` Resource의 `Ammo` 값은 변하지 않음.

## 긍정 확인

- `SkillAmmoState` 자체는 Step 3을 받을 API를 갖추고 있다. `Charge`는 `Unlimited`/음수/빈 id를 무시하고 제한 스킬을
  `remaining=max=ammo`로 등록한다(`Assets/Script/SkillSystem/SkillAmmoState.cs:21`~`:31`).
- `BehaviorTree_TurnAction`은 `TurnAction` Resource를 노출하고 있어 BT 순회 기반 charge source를 구현할 수 있다
  (`Assets/Script/AutoCrawlerBehaviorTree/Action/BehaviorTree_TurnAction.cs:13`~`:14`).
- remaining은 여전히 `SkillDefinition`에 저장되지 않고 `CharacterArticle.SkillAmmoState`가 소유하므로 저장 경계는 유지된다.

## 검증 결과

- `dotnet build AutoCrawler.sln -c Debug` — PASS, warnings 0, errors 0.
- `sk002_step1_ammo_test.tscn` — ALL PASS(40 assertions).
- Step 3 전용 headless 테스트는 없음.

## 완료 조건 대조

- reset 시작 시 제한 스킬 remaining이 `Ammo`로 충전된다: **FAIL**. 실제 reset 호출자가 없다.
- 전투 사이 이월 범위: 현재 Step 3 범위는 battle reset만이며 Expedition은 예약값으로 battle charge 처리해야 하나, 호출자가 없어 미검증.
- reset 시점과 저장 경계 문서화: 부분 충족. ADR/Task에는 의도가 있으나 구현 결과가 Task에 완료 상태로 기록되어 있지 않다.
- ammo 상태가 `SkillDefinition` Resource에 저장되지 않는다: PASS.

## 판정

**미완료(P1 1건, P2 1건).** → **처리 완료 후 완료(2026-07-10).**

현재 워크트리는 Step 1/2 산출물과 Step 3 착수 전 메모 상태로 보인다. 핵심인 battle-start reset 연결과 Step 3 전용 검증이
아직 없으므로 Step 3 완료로 볼 수 없다. P1을 구현하고 self-sufficient `sk002_step3` 테스트를 추가한 뒤 재리뷰가 필요하다.

## 처리 결과

### [P1] battle-start reset 수명주기 연결 — 구현 완료

- `CharacterArticle.ChargeBattleAmmo()` 추가: BT를 **raw 재귀 노드 워크**로 순회해
  `BehaviorTree_TurnAction.TurnAction is TurnAction_Skill`인 노드의 `Definition`을 찾아
  `SkillAmmoState.Charge(def.Id, def.Ammo, def.AmmoResetScope)`를 호출한다. `Unlimited`은 `Charge`가 no-op,
  `Battle`/예약 `Expedition`은 이월 없이 full charge(`Assets/Script/Article/CharacterArticle.cs`).
- **왜 raw 워크인가:** `BehaviorTree.FindNodeByType`는 `Root(=GetChild(0))` 하위 `TreeChildren`만 보고, 그
  `_children`은 `OnTreeChanged`(일부 deferred)로 재구성된다. battle-start 시점의 미갱신/Root 밖 배선에도 안전하도록
  raw `GetChildren()` 재귀로 순회한다. 생산 경로에서 결과는 `FindNodeByType`와 동일(실 스킬 노드는 Root 하위).
- `TurnHelper._Ready()`의 turn-affected 수집 루프에서 각 `CharacterArticle`에 `ChargeBattleAmmo()`를 호출해 실제
  전투 시작 수명주기에 연결(`Assets/Script/TurnHelper.cs`). 이제 BT에 제한 `TurnAction_Skill`이 배선되면 첫 시전
  전에 full charge된다.

### [P2] Step 3 self-sufficient 검증 — 추가 완료

- `Sk002Step3ResetTest`(`sk002_step3_reset_test.tscn`) 신규. `battle_field.tscn`을 로드하되 Ally BT에만 스킬을
  주입하므로 **상대 로스터 드리프트에 비의존**. 14 assertion ALL PASS:
  - A: `ChargeBattleAmmo`가 제한 스킬 full 충전, Unlimited는 entry 없음.
  - **B: `TurnHelper._Ready()`(battle start)가 실제로 충전** — P1 배선을 직접 고정.
  - C: Battle 이월 없음(소모 후 재충전 → full 리셋).
  - D: 소모해도 `SkillDefinition.Ammo`(Resource) 불변 + `Remaining` 필드 없음(저장 경계).
  - E: 예약 `Expedition`이 battle charge로 안전 처리.

## 재검증 결과

- `dotnet build AutoCrawler.sln -c Debug` — PASS, warnings 0, errors 0.
- `sk002_step3_reset_test.tscn` — ALL PASS(14 assertions), B 포함.
- `sk002_step1_ammo_test.tscn` — ALL PASS(40).
- `sk001_step1`/`sk001_step3`/`cb001_step1` — ALL PASS(`TurnHelper._Ready` 변경이 battle-load 경로 회귀 없음 확인).
- 선행 baseline 실패 `sk001_step6` D / `cb001_step4` D는 SK-002와 무관한 `battle_field.tscn` 드리프트로 불변.

## 재판정

**완료.** P1(수명주기 배선)·P2(self-sufficient 검증)가 모두 해소됐다. Step 3 완료 조건(reset 시 제한 스킬
remaining=Ammo, Expedition은 battle charge 처리, reset 시점/저장 경계 문서화, Resource 미저장)이 충족된다.
`TurnHelper._Ready` 변경은 기존 battle-load 테스트에 회귀를 만들지 않는다(생산 유닛엔 `TurnAction_Skill` 배선이 없어
charge가 no-op). Step 4(문서화·완료 리뷰)로 진행 가능하다.

## 재확인 결과(리뷰어, 2026-07-10)

이전 P1/P2는 실제 코드와 실행 결과 기준으로 해소 확인.

- `CharacterArticle.ChargeBattleAmmo()`가 BT raw 재귀 순회로 `BehaviorTree_TurnAction` 안의 `TurnAction_Skill.Definition`을 찾아 `SkillAmmoState.Charge(...)`를 호출한다.
- `TurnHelper._Ready()`의 전투 유닛 수집 루프에서 각 `CharacterArticle`에 `ChargeBattleAmmo()`를 호출하므로 battle-start reset 수명주기에 연결됐다.
- `sk002_step3_reset_test.tscn`이 추가됐고, 특히 B 케이스가 `_Ready` 실제 충전을 고정한다.

### 잔여 P3

- `Assets/Script/Tests/Sk001Step6DataPackTest.cs:241` 주석의 "Step 3 전투 시작 reset이 없으므로" 표현은 이제 낡았다. 실제 동작에는 영향 없지만, "이 테스트는 RunSkill이 battle-start reset 경로 밖에서 직접 TurnAction을 실행하므로 여기서 대역 충전한다" 정도로 바꾸면 더 정확하다.

## 재검증 결과(리뷰어)

- `dotnet build AutoCrawler.sln -c Debug` — PASS, warnings 0, errors 0.
- `godot --headless --path . --import` — exit 0. 기존 `graph_offset deprecated`, ObjectDB/resource leak 종료 로그만 출력.
- `sk002_step3_reset_test.tscn` — ALL PASS(14 assertions).
- `sk002_step1_ammo_test.tscn` — ALL PASS(40 assertions).
- `sk001_step1_skill_system_test.tscn` — ALL PASS.
- `sk001_step3_mana_hit_test.tscn` — ALL PASS.
- `sk001_step6_data_pack_test.tscn` — Step 2/3 관련 A/B/C PASS, 전체 suite는 알려진 baseline 실패 `D.ChainHitsThree -> got 1, expected 3`로 exit 1.

## 최종 재판정

**완료(P3 1건, P0/P1/P2 없음).** Step 3 완료 조건은 충족됐다. 남은 P3는 주석 정확도 문제라 Step 4로 진행을 막지 않는다.

---

# SK-002 Skill Ammo System — Step 4 완료 리뷰

리뷰 일자: 2026-07-10

## 처리 결과

### [P3] `Sk001Step6DataPackTest.cs` stale 주석 — 수정 완료

- Step 3 battle-start reset이 구현됐으므로 "Step 3 전투 시작 reset이 없으므로"는 낡았다. 주석을 "이 테스트는
  RunSkill이 TurnHelper battle-start reset 경로 밖에서 TurnAction을 직접 실행하므로 여기서 대역 충전한다"로
  정정했다(`Assets/Script/Tests/Sk001Step6DataPackTest.cs`). 코드 동작 무변경.

### Step 4 문서 갱신 (완료 조건: ammo 데이터 모델/owner/소모·환불 정책/reset scope/검증 문서화)

- [[Skill-System]]: "Ammo(SK-002, ADR-021)" 절 신규(기존 "Ammo Follow-up" 대체) — 데이터 모델, runtime owner,
  소모/환불 정책(ADR-021 §3), reset scope, battle-start 배선, Phase1 수치, report, 후속을 모두 문서화. Model 필드
  목록(ManaCost/AmmoResetScope/Ammo)·현재 상태·Verification(SK-002 두 테스트 + 선행 실패)도 갱신.
- [[Current-State]]: Combat에 SK-002 전체 완료 엔트리 추가. Known Regressions 배너를 근본 원인 확정
  (`e5d339b` 의도적 상대 교체)으로 정정.
- [[Open-Tasks]]: SK-002를 완료 처리하고 후속(HUD/save/Expedition 실이월/loadout/`TurnAction_Skill` 배선) 명시.
  SK-001 후속의 "무대상/마나 환불 정책 확정"을 ADR-021로 해소 표시. rebaseline Task 등록.
- [[SK-002-Skill-Ammo-System]] Task: 전 스텝 결과 기록, status `complete`.

## 검증 결과 (누적)

- `dotnet build AutoCrawler.sln -c Debug` — PASS, warnings 0, errors 0.
- `sk002_step1_ammo_test`(40) / `sk002_step3_reset_test`(14) — ALL PASS.
- `sk001_step1` / `sk001_step3` / `cb001_step1` — ALL PASS(회귀).
- `sk001_step6` — Step 2/3 관련 A/B/C PASS, 전체 suite는 선행 baseline `D.ChainHitsThree`로 exit 1(SK-002 무관).

## 완료 조건 대조 (Step 4)

- ammo 데이터 모델/runtime owner/소모·환불 정책/reset scope/검증 결과 문서화: **PASS**([[Skill-System]] Ammo 절).
- P0/P1 리뷰 문제 없음: **PASS**.

## 최종 판정 (SK-002 전체)

**완료.** Step 0(설계 리뷰+ADR)~Step 4(문서화) 전부 완료. P0/P1 없음. 잔여 이슈는 SK-002 범위 밖 선행
`battle_field.tscn` 개편 회귀(별도 rebaseline Task)와 ADR-021에 명시된 후속(HUD/save/Expedition 실이월/loadout/
`TurnAction_Skill` 실 BT 배선)뿐이다.

## Step 4 리뷰어 재검토(2026-07-10)

### 발견 사항

1. **[P2] 현재 사실 문서에 SK-002 이전의 무대상/마나 게이트 설명이 남아 있다**
   - [[Skill-System]]의 Ammo 절은 ADR-021 기준으로 정확하지만, 같은 문서의 이전 절들이 여전히 pre-SK-002 계약을 현재 사실처럼 설명한다.
   - `Skill-System.md:29`: "사거리 내 무대상이면 `SelectTarget`이 `null`(현재 no-op로 턴 소모). 무대상 fail-closed는 Step 3에서 ..."라고 되어 있다. 현재 `TurnAction_Skill.Init`은 무대상에서 무소모 `Failure`를 낸다.
   - `Skill-System.md:37`: `ManaCost`가 `Mana.TrySpend`로 즉시 지불되는 것처럼 설명한다. 현재는 `CanAfford`로 가능성만 확인하고 대상 락온 후 커밋에서 `TrySpend`한다.
   - `Current-State.md:113`: SK-001 Step 3 설명 말미에 "무대상 fail-closed는 여전히 후속"이라고 남아 있다. 현재는 SK-002/ADR-021에서 해소됐다.
   - Step 4의 핵심 완료 조건이 현재 사실 문서화이므로, 이 낡은 문장은 수정해야 한다. 권장: `Target Selection` 절은 "무대상은 SK-002 이후 무소모 Failure"로, `Payment Gate` 절은 "legacy Step 3 당시 계약이 SK-002에서 target-first로 재정렬됨" 또는 현재 계약으로 정리한다. `Current-State`의 낡은 후속 문장도 ADR-021 해소로 교체한다.

2. **[P3] 완료된 SK-002가 Open-Tasks `Next`에 남아 있다**
   - `Open-Tasks.md:11`이 `Next` 아래에 "SK-002 전체 완료"를 둔다. 내용 자체는 정확하지만, 이 문서의 Maintenance 규칙은 완료 작업을 목록에서 제거하라고 한다.
   - 권장: SK-002 완료 요약은 `Recently Completed`로 옮기거나 `Current-State`/Task 링크에만 맡기고, `Next`에는 실제 후속 Task 후보(HUD/save/Expedition/loadout/BT 재배선)와 rebaseline Task만 남긴다.

3. **[P3] 완료 Task의 `Current Code Facts`가 pre-SK-002 상태를 말한다**
   - [[SK-002-Skill-Ammo-System]]은 status `complete`인데 `Current Code Facts`에 "SkillDefinition ... ammo 필드는 없다"가 남아 있다.
   - 완료 Task의 과거 맥락으로 볼 수도 있지만 제목이 Current라서 후속 세션을 오도할 수 있다. "착수 당시 코드 사실"으로 rename하거나 완료 후 현재 사실로 정리하는 편이 좋다.

### 검증 결과

- `dotnet build AutoCrawler.sln -c Debug` — PASS, warnings 0, errors 0.
- `sk002_step1_ammo_test.tscn` — ALL PASS(40 assertions).
- `sk002_step3_reset_test.tscn` — ALL PASS(14 assertions).

### 판정

**수정 필요(P2 1건, P3 2건, P0/P1 없음).**

제품 코드와 SK-002 핵심 동작은 완료 상태다. 다만 Step 4는 문서화 완료 Step이므로, 현재 사실 문서 안에 남은 pre-SK-002 설명은 완료 판정 전에 정리하는 것이 맞다. P2 문서 정합성만 고치면 SK-002 전체 완료 판정 가능하다.

## Step 4 문서 정합성 수정 처리 결과(2026-07-10)

### [P2] pre-SK-002 현재 사실 문장 잔존 — 수정 완료

- [[Skill-System]] `Target Selection` 절을 현재 계약으로 수정했다. 사거리 내 무대상은 `SelectTarget == null` 뒤 커밋 이전 무소모 `ActionState.Failure`를 내고 BT Selector fallback을 탄다.
- [[Skill-System]] `Payment Gate and Hit Chance` 절을 현재 계약으로 수정했다. `ManaCost > 0`은 먼저 `Mana.CanAfford`로 가능성만 확인하고, 실제 `TrySpend`는 대상 락온(커밋) 뒤 ammo와 함께 수행한다.
- [[Current-State]]의 SK-001 Step 3 과거 설명 말미를 수정했다. 무대상 fail-closed/마나 환불 정책은 이제 SK-002/ADR-021에서 target-first 무소모 Failure/커밋 후 무환불로 확정된 상태다.

### [P3] 완료된 SK-002가 Open-Tasks `Next`에 남음 — 정리 완료

- [[Open-Tasks]] `Next`에는 실제 후속인 "SK-002 후속 — ammo UI/영속/loadout 실효화"만 남겼다.
- SK-002 본체 완료 요약은 `Recently Completed`로 이동했다.

### [P3] 완료 Task의 `Current Code Facts` 착수 전 표현 — 정리 완료

- [[SK-002-Skill-Ammo-System]]의 `Current Code Facts`를 `Starting Code Facts`로 변경했다.
- ammo 필드 부재/마나 게이트 설명은 착수 당시 사실임을 명시하고, 현재 사실은 [[Skill-System]] "Ammo" 절을 참조하도록 정리했다.

## 문서 검증

- `rg`로 다음 낡은 표현이 남지 않음을 확인: `무대상 fail-closed는 여전히 후속`, `현재 no-op로 턴 소모`, `현재 필드에는 .*ammo 필드는 없다`, `## Current Code Facts`, `SK-002 Skill Ammo System — 전체 완료`, `후속 Task 후보`.
- [[Open-Tasks]] `Next` 구조 확인: SK-002 본체 완료 항목은 사라지고 후속 Task/rebaseline Task만 남음.
- [[SK-002-Skill-Ammo-System]] 상단 확인: `Starting Code Facts`로 정리됨.

## Step 4 최종 재판정

**완료.** P2/P3 문서 정합성 지적은 모두 해소됐다. SK-002 Step 0~4 전체 완료 판정을 유지한다. 남은 항목은 SK-002 범위 밖 후속(ammo HUD/save/Expedition 실이월/loadout/실 BT 배선)과 별도 rebaseline Task뿐이다.
