---
type: review
task: ST-001
status: complete
reviewed: 2026-07-08
---

# ST-001 Natural Regen Stats Review

ST-001 Step 0 설계 리뷰다. 이번 세션은 제품 코드와 리소스를 수정하지 않고, Task
[[ST-001-Natural-Regen-Stats]], ADR [[ADR-019-Natural-Regen-Stats]], 관련 시스템 문서
[[Article-Status-System]]/[[Turn-System]]/[[Skill-System]], 실제 전투/상태 코드를 대조했다.

## Findings

1. **[P1] 지속 효과 뒤 자연 회복 순서는 같은 hook 안의 사망 후 HP 회복을 만들 수 있다 — 설계 수정함**
   - Task 초안은 `StatusController.OnTurnStart()` → `ArticleStatus.ApplyAffectingStatuses()` →
     natural regen 순서를 제안했다.
   - 실제 `Health.CurrentHealth` setter는 입력값이 0 이하이면 clamp 전에 `Owner.Dead()`를 호출한다
     (`Assets/Script/Article/Status/Element/Health.cs:22`). `CharacterArticle.ApplyTurnStartEffects()`는
     현재 `StatusController.OnTurnStart()` 후 `ArticleStatus.ApplyAffectingStatuses()`를 호출한다
     (`Assets/Script/Article/CharacterArticle.cs:53`).
   - 이 구조에서 지속 피해가 사망을 발생시킨 직후 같은 턴 시작 hook에서 양수 `HealthRegen`을 적용하면,
     `OnDead`/`QueueFree`가 이미 시작된 유닛의 HP를 다시 양수로 올릴 수 있다.
   - **수정:** Task/ADR의 확정 순서를 `StatusController.OnTurnStart()` → natural regen →
     살아 있으면 `ArticleStatus.ApplyAffectingStatuses()`로 바꿨다. 자연 회복 또는 지속 효과로 사망한
     유닛은 같은 hook 안에서 회복으로 사망을 되돌리지 않는 완료 조건을 추가했다.

2. **[P2] production scene 배선은 중복 StatusElement 예외를 직접 검증해야 한다**
   - `ArticleStatus.InitStatus()`는 export 배열을 순회하며 `StatusElementsDictionary.Add(stat.GetType(), stat)`를
     호출한다(`Assets/Script/Article/Status/ArticleStatus.cs:20`). 같은 타입이 중복되면 초기화 중 예외가 난다.
   - Step 2는 여러 character scene의 `StatusElements` 배열을 직접 수정하므로, 수동 편집/저장 과정에서 중복
     `Mana`/`ManaRegen`/`HealthRegen`이 들어가지 않는지 씬 로드와 `--import`로 확인해야 한다.
   - **권장:** Step 2 테스트에 각 production 캐릭터별 dictionary key set 또는 scene load 후 중복 없음 검증을
     포함한다. Task의 Risk는 이미 이 지점을 언급하고 있어 Step 구조 자체는 유지 가능하다.

3. **[P3] 테스트 fixture에서 export 배열과 runtime dictionary 경로를 모두 대표해야 한다**
   - 기존 SK 테스트들은 `StatusElementsDictionary[typeof(Mana)] = mana` 식으로 runtime dictionary에 직접
     스탯을 주입하는 패턴이 있다. 이 방식은 빠른 통합 테스트에는 좋지만, 새 `[GlobalClass]` 스탯이 실제
     `.tscn`/`.tres` export 배열에 저장/로드되는 경로를 대표하지 않는다.
   - **권장:** Step 1은 helper/pure 경로에서 dictionary fixture를 써도 되지만, Step 2는 실제 scene
     저장/재로드 또는 최소한 loaded scene의 export 배열 기반 `InitStatus()` 경로를 검증한다.

## Open Decisions

- **적용 순서:** natural regen을 지속 효과보다 먼저 적용하는 것으로 확정했다. 이는 "턴 시작 기본 회복 후
  독/출혈이 최종 생존을 결정"하는 의미이며, 사망 후 부활성 회복을 막는 보수적 선택이다.
- **음수 회복:** 허용한다. 음수 `HealthRegen`이 사망을 만들 수 있으며, 이때 같은 hook 안에서 사망을 되돌리지
  않는다.
- **누락 스탯:** no-op로 확정한다. `HealthRegen`만 있고 `Health`가 없거나, `ManaRegen`만 있고 `Mana`가 없으면
  SCRIPT ERROR 없이 아무 것도 하지 않는다.
- **production 기본값:** Task의 초안 수치를 Step 2 시작점으로 둔다. 수치가 CB-001 이벤트 로그를 바꾸면 기대값
  갱신 필요성을 명시한다.

## Step Assessment

- **Step 0:** 완료 가능. P1 순서 문제를 Task/ADR에 반영했으므로 구현으로 넘어갈 수 있다.
- **Step 1:** 범위가 적절하다. product scene을 건드리지 않고 StatusElement와 턴 시작 적용 helper를 고정하는
  분리가 좋다. 완료 조건에 사망 후 회복 금지와 Speed/frame 불변을 포함해야 한다.
- **Step 2:** production scene 배선은 별도 Step으로 분리한 것이 맞다. `.tscn` 재직렬화 범위와 중복 스탯 검증,
  `Mana.CurrentMana == MaxMana` 초기화 확인이 핵심이다.
- **Step 3:** 문서/완료 리뷰 Step으로 적절하다. `Article-Status-System`, `Turn-System`, `Skill-System`,
  `Current-State`, `Open-Tasks` 갱신이 필요하다.

## Verification Assessment

필수 자동 검증:

- `HealthRegen`/`ManaRegen` 값이 턴 시작 1회만 적용된다.
- Max clamp, 음수 감소, 누락 `Health`/`Mana` no-op.
- 음수 `HealthRegen` 사망과 지속 피해 사망 뒤 회복 부활 방지.
- Running frame 반복과 `TurnHelper.Speed` 변화가 적용 횟수를 늘리지 않는다.
- `ManaCost` 지불 후 다음 턴 `ManaRegen` 회복.
- Step 2에서 production 캐릭터 scene load/save/reload, 중복 StatusElement 없음, `Mana` 초기값 Max 보장.
- CB-001 Step 1~4와 SK-001 마나 관련 회귀 유지, Godot headless `--import` exit 0.

## Verdict

**Approved after design fixes** — `HealthRegen`/`ManaRegen`을 별도 `StatusElement`로 두고 턴 시작 1회 적용하는
방향은 현재 `TurnHelper`/`CharacterArticle` 구조와 잘 맞는다. 설계 리뷰 중 발견한 P1 적용 순서 문제를
Task/ADR에 반영했으므로 Step 1 구현으로 진행 가능하다. 제품 코드와 리소스는 수정하지 않았다.

## Related

- [[ST-001-Natural-Regen-Stats]]
- [[ADR-019-Natural-Regen-Stats]]
- [[Article-Status-System]]
- [[Turn-System]]
- [[Skill-System]]
- [[STEP_REVIEW_WORKFLOW]]

# ST-001 Step 1 Rework Review (2026-07-08)

## Findings

1. **[P1] Step 1 headless test fails because the turn-start affect fixture applies immediately during setup**
   - `TestCharacterTurnStartOrderAndDeathGuard()` registers `TestHealthDeltaAffect` with `ArticleStatus.ApplyAffectStatus()` and expects it to run later during `ApplyAffectingStatuses()`.
   - The fixture class implements `IAffectedImmediately`, so `ApplyAffectStatus()` applies it immediately at setup time. The first case kills the unit before `deadCount` is subscribed; the second case heals before natural regen runs, so the expected natural-death/skip path is not tested.
   - Observed run: `st001_step1_natural_regen_test.tscn` exits 1 with 3 failed assertions: `D.DeadOnce`, `D.NaturalDeath`, `D.AffectSkippedAfterNaturalDeath`.
   - Relevant code: `Assets/Script/Tests/St001Step1NaturalRegenTest.cs:173`, `:187`, `:440`, `:452`.
   - Required fix: use a turn-start/persistent test affect that is stored first and only mutates during `ApplyAffectingStatuses()` (for example implement `IAffectedOnlyMyTurn` instead of `IAffectedImmediately`, or create an explicit non-immediate test affect matching `StatusAffect.Apply()` semantics). Then rerun the headless scene.

2. **[P2] Test fixture emits repeated non-benign HealthBar method errors**
   - `MakeArticle()` assigns a plain `ProgressBar` as `HealthBar`, but `Health.OnInit()` and `Health.CurrentHealth` call `init_health`/`_set_health` on that node.
   - The headless run prints repeated `Invalid call. Nonexistent function 'init_health'/'_set_health' in base 'Godot.ProgressBar'` errors.
   - This does not explain the three failed assertions, but Step 1 verification should be non-noisy so real script errors are not hidden.
   - Relevant code: `Assets/Script/Tests/St001Step1NaturalRegenTest.cs:314`, `:320`; production calls in `Assets/Script/Article/Status/Element/Health.cs:25`, `:32`.
   - Required fix: use the real HealthBar scene/script or a tiny test double node implementing `init_health` and `_set_health`.

## Positive Notes

- Product code now follows the reworked responsibility boundary: `ArticleStatus` dispatches `ITurnStartStatusElement` by order and does not directly reference `HealthRegen`/`ManaRegen` in product code.
- `CharacterArticle.ApplyTurnStartEffects()` keeps the approved order: `StatusController.OnTurnStart()` -> turn-start status elements -> alive guard -> `ApplyAffectingStatuses()`.
- `dotnet build AutoCrawler.sln -c Debug` passed with 0 warnings and 0 errors.

## Verification Performed

- `dotnet build AutoCrawler.sln -c Debug` — PASS, warnings 0, errors 0.
- Godot headless `Assets/Script/Tests/st001_step1_natural_regen_test.tscn` — FAIL, 3 assertions failed plus repeated HealthBar method errors.
- Static search confirmed no product `ApplyNaturalRegen` usage remains and product `ArticleStatus` has no direct `HealthRegen`/`ManaRegen` identifier references.

## Verdict

**미완료 / Rework required.** Responsibility-boundary rework direction is correct, but Step 1 cannot be approved while its dedicated headless test fails. Fix the test fixture (and HealthBar test double noise), rerun the headless test and required regressions, then request review again.

# ST-001 Step 1 Rework Fix Verification (2026-07-08)

## Findings

P0/P1/P2 unresolved findings 없음.

이전 리뷰 지적 처리 확인:

- **[P1] turn-start affect fixture 즉시 적용 문제 — 수정 확인.** `TestHealthDeltaAffect`가 `IAffectedImmediately`에서 `IAffectedOnlyMyTurn`로 바뀌어 `ApplyAffectStatus()` 시점에는 등록만 되고, 실제 변경은 `ApplyAffectingStatuses()`에서 발생한다. `D.DeadOnce`/`D.NaturalDeath`/`D.AffectSkippedAfterNaturalDeath`가 headless에서 모두 PASS했다.
- **[P2] HealthBar method-not-found 에러 — 수정 확인.** `test_health_bar.gd` test double이 `init_health`/`_set_health`를 제공하고, ST-001 전용 headless 실행에서 이전 `ProgressBar` method-not-found ERROR가 재발하지 않았다.

## Verification Performed

- `dotnet build AutoCrawler.sln -c Debug` — PASS, warnings 0, errors 0.
- `godot --headless --path . --import` — exit 0. 기존 `graph_offset deprecated`, ObjectDB/resource leak 종료 로그는 남음.
- `st001_step1_natural_regen_test.tscn` — ALL PASS.
- CB-001 회귀:
  - `cb001_step1_turn_effect_test.tscn` — ALL PASS.
  - `cb001_step2_combat_rng_test.tscn` — ALL PASS.
  - `cb001_step3_canonical_order_test.tscn` — ALL PASS.
  - `cb001_step4_determinism_test.tscn` — ALL PASS. 기존 animation missing/ObjectDB leak 로그는 남음.
- SK-001 대표 회귀:
  - `sk001_step3_mana_hit_test.tscn` — ALL PASS.
  - `sk001_step6_data_pack_test.tscn` — ALL PASS.
- Static check: product `ArticleStatus`는 `ITurnStartStatusElement` dispatch만 수행하고 `HealthRegen`/`ManaRegen` 구체 타입을 직접 분기하지 않는다.

## Remaining Risks

- Step 2 production scene 배선 전까지 실제 캐릭터 `.tscn` export 배열 기반 저장/재로드 검증은 아직 없다.
- `ArticleStatus.ApplyAffectingStatuses()`의 foreach 중 만료 affect 제거 문제는 Step 1 범위 밖의 잠재 결함으로 남는다. 현재 production `IAffectedOnlyMyTurn` 구현이 없어 이번 변경의 회귀는 아니지만 별도 Task 후보로 유지한다.
- `ArticleBase.IsAlive`의 Health indexer 예외와 `ArticleStatus.HasLivingHealth()` 개념 중복은 Step 1 범위 밖 P3로 남는다.

## Verdict

**수정 후 완료.** Step 1 rework 완료 조건을 충족하고, 이전 P1/P2 지적은 재검증으로 해소됐다. Step 2(production character Mana/Regen wiring)로 진행 가능하다.

# ST-001 Step 2 Code Review (2026-07-08)

## Findings

1. **[P1] battle_field 인스턴스의 ArticleStatus override에는 Mana/Regen이 없어 production 전투 경로가 배선되지 않았다**
   - Step 2는 production 캐릭터 씬에 `Mana`/`ManaRegen`/`HealthRegen`을 배선하고, `battle_field.tscn`에서 `ManaCost` 지불 후 다음 턴 회복을 검증해야 한다.
   - `battle_field.tscn`은 `TempArticle3.tscn`/`Puppet.tscn`을 인스턴스하지만, 각 인스턴스의 `ArticleStatus`를 로컬 `SubResource`로 override한다. 이 로컬 `StatusElements` 배열은 기존 5개 스탯만 들고 있어 원본 씬에 추가한 `Mana`가 전투 씬에 전파되지 않는다.
   - 실제 headless 결과: `st001_step2_production_wiring_test.tscn`의 `D.CasterHasProductionMana`가 `False`로 실패했다.
   - 관련 위치: `Assets/Scenes/Map/battle_field.tscn:49`, `:76`, `:103`, `:130`, `:157`; Ally 인스턴스 override는 `Assets/Scenes/Map/battle_field.tscn:284`-`:286`.
   - 권장 수정: `battle_field.tscn`의 로컬 ArticleStatus sub-resource들에도 새 `Mana`/`ManaRegen`/`HealthRegen`을 추가하거나, 의도적으로 원본 씬의 ArticleStatus를 쓰도록 override를 제거/재정렬한다. 수정 후 Step 2 전용 테스트와 CB-001 결정론 회귀를 다시 실행해야 한다.

2. **[P1] Step 2 전용 테스트의 2회 회복 기대값이 Mana clamp 정책과 충돌해 headless가 실패한다**
   - `Mana.CurrentMana` setter는 값을 `0..MaxMana`로 clamp한다.
   - 테스트는 `MaxMana - 10`에서 시작해 두 번 회복한 값을 `MaxMana - 10 + ManaRegen * 2`로 기대한다. `PrincessKnight`와 `TempArticle3`은 `ManaRegen = 8`이므로 두 번째 회복 시 66을 기대하지만 실제 정책상 60으로 clamp된다.
   - 실제 headless 결과: `B.PrincessKnight.ManaRegeneratedTwiceOnSecondTurn`, `B.TempArticle3.ManaRegeneratedTwiceOnSecondTurn` 실패.
   - 관련 위치: `Assets/Script/Tests/St001Step2ProductionWiringTest.cs:116`-`:121`; clamp 정책은 `Assets/Script/Article/Status/Element/Mana.cs:18`.
   - 권장 수정: 두 번 회복 검증은 `MaxMana - ManaRegen * 2`처럼 clamp에 걸리지 않는 시작값을 쓰거나, 두 번째 기대값을 `Math.Min(MaxMana, start + ManaRegen * 2)`로 계산한다. 별도 max clamp 검증은 현재처럼 유지하면 된다.

## Positive Notes

- 원본 캐릭터 씬 4개(`PrincessKnight`, `Puppet`, `TempArticle2`, `TempArticle3`)의 직접 인스턴스 배선은 테스트 [A]/[B]/[C] 초반에서 확인됐다.
- 씬 저장/재로드 round-trip 테스트 [C]는 4개 원본 씬 모두 새 스탯과 수치를 보존했다.
- `dotnet build AutoCrawler.sln -c Debug`는 경고 0, 오류 0으로 통과했다.
- `godot --headless --path . --import`는 exit 0으로 통과했다. 기존 `graph_offset deprecated`, ObjectDB/resource leak 종료 로그는 남아 있다.

## Verification Performed

- `dotnet build AutoCrawler.sln -c Debug` — PASS, warnings 0, errors 0.
- `godot --headless --path . --import` — exit 0.
- `st001_step2_production_wiring_test.tscn` — FAIL, 3 assertions failed:
  - `B.PrincessKnight.ManaRegeneratedTwiceOnSecondTurn`
  - `B.TempArticle3.ManaRegeneratedTwiceOnSecondTurn`
  - `D.CasterHasProductionMana`

## Verdict

**미완료 / Rework required.** 원본 캐릭터 씬 배선 자체는 상당 부분 맞지만, 실제 production 전투 씬(`battle_field.tscn`)의 로컬 `ArticleStatus` override가 Step 2 핵심 완료 조건을 깨고 있다. 전용 headless 테스트도 실패하므로 Step 3로 진행하면 안 된다.

# ST-001 Step 2 Rework Fix Verification (2026-07-08)

## Findings

P0/P1/P2 unresolved findings 없음.

이전 Step 2 리뷰 지적 처리 확인:

- **[P1] battle_field 인스턴스 ArticleStatus override 배선 누락 — 수정 확인.** `battle_field.tscn`의 Ally 1개와 Opponent 4개 로컬 `ArticleStatus` override에 `Mana`/`ManaRegen`/`HealthRegen`이 추가됐고, Step 2 전용 테스트 `[D]`가 실제 `battle_field` 인스턴스 5개 전부의 배선과 수치를 확인했다.
- **[P1] 2회 회복 기대값의 max clamp 충돌 — 수정 확인.** Step 2 테스트 `[B]`가 clamp에 걸리지 않는 시작값(`MaxMana - (ManaRegen * 2 + 1)`)을 사용해 2회 회복을 검증하고, 별도 `ManaClampedAtMax`로 max clamp를 유지 검증한다.

## Verification Performed

- `dotnet build AutoCrawler.sln -c Debug` — PASS, warnings 0, errors 0.
- `godot --headless --path . --import` — exit 0. 기존 `graph_offset deprecated`, ObjectDB/resource leak 종료 로그는 남음.
- `st001_step2_production_wiring_test.tscn` — ALL PASS.
- `st001_step1_natural_regen_test.tscn` — ALL PASS.
- CB-001 회귀:
  - `cb001_step1_turn_effect_test.tscn` — ALL PASS.
  - `cb001_step2_combat_rng_test.tscn` — ALL PASS.
  - `cb001_step3_canonical_order_test.tscn` — ALL PASS.
  - `cb001_step4_determinism_test.tscn` — ALL PASS. 기존 `Walk`/`Attack` animation missing 로그와 ObjectDB leak 종료 로그는 남음.
- SK-001 대표 회귀:
  - `sk001_step3_mana_hit_test.tscn` — ALL PASS.
  - `sk001_step6_data_pack_test.tscn` — ALL PASS.

## Remaining Risks

- `battle_field.tscn`이 캐릭터별 `ArticleStatus`를 로컬 override하는 구조는 스탯 추가 시 원본 씬과 전투 씬을 둘 다 수정해야 하므로 깨지기 쉽다. 이번 Step은 테스트로 보강했지만 구조 정리는 별도 후속 후보로 남긴다.
- 기존 Godot 종료 로그(ObjectDB/resource leak)와 CB-001 Step 4의 animation missing 로그는 이번 변경과 무관하게 계속 남는다.
- 전투 UI 마나 바는 Step 2 범위 밖이다.

## Verdict

**수정 후 완료.** Step 2 완료 조건을 충족하고 이전 P1 2건은 재검증으로 해소됐다. Step 3(문서/완료 리뷰)로 진행 가능하다.

---

# ST-001 Completion Review (Step 3)

Step 3은 문서 갱신과 완료 판정만 수행한다. 제품 코드와 리소스를 수정하지 않았다.

## Delivered

- **Model:** `HealthRegen`/`ManaRegen : StatusElement`(`[Export] int Value`, 기본 0). `Health`/`Mana`는 현재/최대량 저장소로 유지.
- **Contract:** `ITurnStartStatusElement`(`TurnStartOrder`, `ApplyTurnStart(ArticleStatus)`). `HealthRegen` = 100, `ManaRegen` = 110.
- **Dispatch:** `ArticleStatus.ApplyTurnStartStatusElements()`가 `TurnStartOrder` 오름차순(동점자는 타입 `FullName` ordinal)으로 실행하고, 각 스탯 실행 직전 `HasLivingHealth()`로 사망 유닛을 차단한다. 구체 regen 타입 지식 없음.
- **Timing:** `CharacterArticle.ApplyTurnStartEffects()` = `StatusController.OnTurnStart()` → turn-start 스탯 → 살아 있으면 `ApplyAffectingStatuses()`.
- **Policy:** `Value == 0`/대상 스탯 누락 no-op, 음수는 감소, 사망을 같은 hook에서 되돌리지 않음, RNG 미소비.
- **Production wiring:** 캐릭터 씬 4종 + `battle_field.tscn` override 5개(Ally = TempArticle3 60/8/0, Opponent 4명 = Puppet 30/3/0). `HealthRegen`은 전부 0.

## Step 목표 대비 판정

| Step | 판정 |
| --- | --- |
| Step 0 설계 리뷰 | Approved after design fixes(적용 순서 P1 반영) |
| Step 1 스탯 + 턴 시작 적용 | 완료(rework 1회. 리뷰 P1/P2는 테스트 fixture 결함으로 수정) |
| Step 2 production 배선 | 수정 후 완료(리뷰 P1 2건: `battle_field.tscn` override 누락, clamp 충돌 기대값) |
| Step 3 문서/완료 리뷰 | 완료 |

Task Goal의 목표 상태 4개(스탯 보유, 턴당 정확히 1회, 프레임/애니메이션/`Speed` 불변, `ManaCost` 스킬과 함께 production 배선)를 모두 만족한다.

## Verification Performed

Step 2 재검증 시점의 실행 결과가 최종 근거다(Godot 4.6.3 Mono headless).

- `dotnet build AutoCrawler.sln -c Debug` — 경고 0, 오류 0.
- `godot --headless --path . --import` — exit 0. 새 `[GlobalClass]` `HealthRegen`/`ManaRegen` 등록.
- `st001_step1_natural_regen_test.tscn` — ALL PASS.
- `st001_step2_production_wiring_test.tscn` — ALL PASS.
- CB-001 Step 1~4 — ALL PASS(결정론 회귀 유지, 기대 로그 갱신 불필요).
- SK-001 Step 3/6 — ALL PASS.

Step 3에서는 문서만 갱신했으므로 추가 실행 검증이 없다.

## Documentation Updated

- [[Article-Status-System]]: turn-start StatusElement 계약, 회복 정책, production 배선 표, `battle_field.tscn` override 주의, 검증 자산.
- [[Turn-System]]: 턴 시작 hook 3단계 순서와 자연 회복의 RNG 미소비.
- [[Skill-System]]: `Mana` production 배선 완료로 사실 갱신.
- [[Current-State]], [[Open-Tasks]].

## Remaining Risks / Follow-ups

- ~~**[별도 Task]** `ArticleStatus.ApplyAffectingStatuses()`는 순회 중인 `AffectingStatusesList`를 만료 콜백이 수정한다.~~ → 완료 리뷰 직후 별도 Task 없이 수정했다(snapshot 순회 + 회귀 테스트 `[H]`).
- ~~**[P3]** `ArticleBase.IsAlive`가 `StatusElementsDictionary[typeof(Health)]` indexer라 `Health` 없는 Article에서 예외를 던진다.~~ → 완료 리뷰 직후 별도 Task 없이 수정했다(`HasLivingHealth()` 위임 + 회귀 테스트 `[I]`).
- **[P3]** `battle_field.tscn`의 per-instance `ArticleStatus` override 구조는 같은 수치를 5곳에 중복시킨다. 스탯 추가 시 깨지기 쉽다.
- **[P3]** `TurnStartOrder`가 각 클래스 하드코딩 상수라 order 표를 한곳에서 볼 수 없다.
- 전투 UI 마나 바/회복량 표시, 아웃게임 성장·장비 연결, 회복 비율 공식은 Out of Scope.

## Verdict

**완료.** Step 0~3의 완료 조건을 모두 충족하고 P0/P1 문제가 없다. 남은 항목은 모두 P3 또는 별도 Task다.

---

# ST-001 Step 3 Documentation Review (2026-07-08)

## Findings

1. **[P2] Current-State에 `Mana production scene 배선은 후속`이라는 오래된 문구가 남아 현재 사실과 충돌한다**
   - `LLM_WIKI/00_Index/Current-State.md:58`은 SK-001 Step 3 기록에서 "Mana의 production scene 배선과 무대상 fail-closed는 후속"이라고 적고 있다.
   - 같은 문서의 ST-001 완료 항목(`Current-State.md:73`-`:78`)과 [[Skill-System]]은 `Mana` production 배선이 ST-001 Step 2에서 완료됐다고 올바르게 기록한다.
   - Current-State는 현재 사실의 진입점이므로, 같은 문서 안에서 완료된 작업을 후속으로도 표시하면 다음 Agent가 Open-Tasks를 잘못 해석할 수 있다.
   - 권장 수정: `Current-State.md:58`을 "당시 Mana production scene 배선은 후속이었고, 현재는 ST-001에서 완료됨. 무대상 fail-closed는 후속"처럼 시점이 드러나게 바꾸거나, `Mana production scene 배선` 문구를 제거한다.

## Positive Notes

- [[Article-Status-System]], [[Turn-System]], [[Skill-System]]은 ST-001의 현재 동작과 production 배선 상태를 대체로 정확히 반영한다.
- [[Open-Tasks]]는 ST-001을 Recently Completed로 옮기고, 남은 `ApplyAffectingStatuses()` 결함과 P3 항목을 별도 후속으로 분리했다.
- Completion Review 자체는 모델, dispatch, timing, production wiring, 검증 결과, remaining risks를 모두 포함한다.

## Verification Performed

- 문서 대조: [[ST-001-Natural-Regen-Stats]], [[ST-001-Natural-Regen-Stats-Review]], [[Article-Status-System]], [[Turn-System]], [[Skill-System]], [[Current-State]], [[Open-Tasks]].
- 정적 검색: ST-001 관련 완료/후속/미실행/리뷰 대기 문구, `Mana production` 잔여 문구, 코드 심볼(`HealthRegen`, `ManaRegen`, `ITurnStartStatusElement`, `ApplyTurnStartStatusElements`).
- 이번 Step 3 리뷰에서는 headless 테스트를 재실행하지 않았다. Step 3가 문서 전용이고, 직전 Step 2 검증 기록(`dotnet build`, `--import`, ST-001, CB-001, SK-001)이 완료 리뷰에 남아 있음을 확인했다.

## Verdict

**수정 필요.** P0/P1은 없지만 Current-State의 현재 사실 충돌(P2)을 정리하기 전에는 Step 3 완료 판정을 그대로 승인하기 어렵다.


# ST-001 Step 3 Documentation Review Fix Verification (2026-07-08)

## Findings

P0/P1/P2 unresolved findings 없음.

이전 문서 리뷰 지적 처리 확인:

- **[P2] Current-State의 오래된 `Mana production scene 배선은 후속` 문구 — 수정 확인.** `LLM_WIKI/00_Index/Current-State.md`의 SK-001 Step 3 항목이 당시 사실과 현재 사실을 구분하도록 바뀌었다. 현재 문구는 당시 `Mana` production scene 배선은 후속이었고, 현재는 ST-001 Step 2에서 완료됐으며, 무대상 fail-closed만 여전히 후속이라고 명시한다.

## Verification Performed

- `Current-State.md`에서 SK-001 Step 3 문구와 ST-001 완료 항목을 대조했다.
- [[Skill-System]]의 `Mana` production 배선 완료 문구와 모순이 없음을 확인했다.
- [[ST-001-Natural-Regen-Stats]], [[Open-Tasks]], [[ST-001-Natural-Regen-Stats-Review]] frontmatter가 `complete` 상태임을 확인했다.
- Step 3는 문서 전용 재확인이므로 headless 테스트는 재실행하지 않았다. 직전 Step 2 검증 기록은 유지된다.

## Verdict

**수정 후 완료.** Step 3 문서 리뷰 P2가 해소됐고, ST-001 Step 0~3 완료 판정을 유지할 수 있다.

# ST-001 Post-Completion Follow-up Verification (2026-07-08)

## Scope

완료 리뷰 직후 별도 Task 없이 처리한 두 후속 수정의 headless 재검증이다.

- `ArticleStatus.ApplyAffectingStatuses()`가 `AffectingStatusesList.ToArray()` snapshot을 순회해, 적용 중 만료된 effect가 리스트를 수정해도 열거가 깨지지 않는지 확인한다.
- `ArticleBase.IsAlive`가 `ArticleStatus.HasLivingHealth()`에 위임해, `Health` 없는 Article과 사망 Article의 생존 판정이 기존 의도와 맞는지 확인한다.

## Findings

P0/P1/P2 unresolved findings 없음.

- `[H] Affect expiring during ApplyAffectingStatuses does not break iteration` — PASS. 만료 effect와 지속 effect가 같은 턴에 모두 1회 적용되고, 만료 effect는 다음 턴에 재적용되지 않으며 지속 effect는 다음 턴에도 적용된다.
- `[I] IsAlive delegates to HasLivingHealth` — PASS. `Health` 없는 Article은 alive, Health 1은 alive, Health 0은 not alive로 판정된다.

## Verification Performed

- `dotnet build AutoCrawler.sln -c Debug` — PASS, warnings 0, errors 0.
- `godot --headless --path . --import` — exit 0. 기존 `graph_offset deprecated`, ObjectDB/resource leak 종료 로그만 출력.
- `Assets/Script/Tests/st001_step1_natural_regen_test.tscn` — ALL PASS(`[H]`/`[I]` 포함).
- CB-001 Step 1~4 — ALL PASS. Step 4의 기존 `Walk`/`Attack` animation missing 로그는 재현되나 테스트 판정 PASS.
- SK-001 Step 3/6 — ALL PASS. 기존 ObjectDB 종료 경고만 출력.

## Verdict

**수정 후 완료.** snapshot 순회와 `IsAlive` 위임 후속 수정의 headless 검증 공백은 닫혔다. ST-001 완료 판정을 유지한다.
