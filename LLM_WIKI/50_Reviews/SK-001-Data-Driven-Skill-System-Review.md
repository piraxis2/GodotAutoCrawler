---
type: review
task: SK-001
status: step1-fix-then-complete
reviewed: 2026-07-07
---

# SK-001 Data-Driven Skill System Review

SK-001 Step 0 설계 리뷰다. 이번 세션은 제품 코드와 리소스를 수정하지 않고, Task
[[SK-001-Data-Driven-Skill-System]], ADR [[ADR-018-Data-Driven-Skill-System]], 참조 기획서
`F:/beestation/ref/wizards_climber_wiki/기획서/10_스킬시스템_설계.md`, 실제 전투/BT 코드를 대조했다.

## Findings

1. **[P1] SkillState 저장 위치와 legacy TurnAction 계약 변경이 Step 1 범위에 명시돼야 한다**
   - Task/ADR은 `ActionQueue`, used cost, target을 `SkillState`로 이동한다고 하지만, 실제
     `TurnActionBase.Action()`은 non-virtual이고 `_usedCost`를 private 필드로 직접 증가시킨다
     (`Assets/Script/TurnAction/TurnActionBase.cs:37`, `:72`, `:80`).
   - `BehaviorTree_TurnAction`은 선택 시마다 `TurnAction.Init(this)`를 호출하고 Running이면 같은
     Resource 인스턴스를 `CharacterArticle.CurrentTurnAction`에 저장한다
     (`Assets/Script/AutoCrawlerBehaviorTree/Action/BehaviorTree_TurnAction.cs:22`, `:27`).
     재개는 `CharacterArticle.CurrentTurnAction.Action(...)`으로 들어온다
     (`Assets/Script/Article/CharacterArticle.cs:46`, `:49`).
   - 이 계약을 그대로 두면 `TurnAction_Skill`이 `SkillState`에 phase queue/used cost를 두더라도 base
     `_usedCost`와 Resource-level queue가 계속 실행 의미를 갖거나, 반대로 Running 재개 시 SkillState를
     찾을 공식 저장 위치가 없다.
   - **설계 수정:** Step 1 범위에 "legacy TurnAction compatibility seam"을 포함한다. 최소 변경은
     `TurnActionBase`/`BehaviorTree_TurnAction`/`CharacterArticle` 중 하나에 유닛별 실행 handle 또는
     state hook을 추가해 `TurnAction_Skill`의 현재 실행 상태가 Resource 필드가 아니라 `SkillState`로만
     복원되도록 하는 것이다. 기존 `CurrentTurnAction` 경로와 legacy 3종 동작은 보존한다.

2. **[P2] Step 1 baseline 비교는 HP뿐 아니라 RNG 소비 순서와 애니메이션 phase 차이를 관찰해야 한다**
   - 기존 `Attack`은 Start/Run/End 3 phase이며 End에서 `Damage.CreateDamage<PhysicalDamage>`를 호출한다
     (`Assets/Script/TurnAction/Common/TurnAction_Attack.cs:21`, `:48`, `:53`).
   - `PhysicalDamage`는 생성 시 크리티컬 RNG를 1회, 피해 계산 시 RNG를 1회 소비한다
     (`Assets/Script/Article/Status/Affect/PhysicalDamage.cs:23`, `:31`).
   - Step 1 완료 조건은 HP 변화/크리티컬/RNG 소비 순서 일치를 이미 말하지만, 테스트가 "최종 HP만" 보면
     같은 결과를 다른 순서로 만드는 회귀를 놓칠 수 있다.
   - **설계 수정:** Step 1 테스트는 같은 seed에서 기존 Attack과 데이터 기반 베기의 phase completion 지점,
     CombatRng 소비 개수/순서, 최종 HP를 함께 비교한다.

3. **[P2] `SkillContext` v0 adapter가 block API와 legacy API 사이의 우선순위를 더 명시해야 한다**
   - ADR은 block이 `SkillContext`로만 세계를 본다고 정했지만, Step 1 `DamageBlock`은 기준선 일치를 위해
     `Damage.CreateDamage<T>()`를 재사용한다. 이 경로는 내부에서 `BattleFieldScene.BattleField.TurnHelper`를
     직접 조회한다.
   - **설계 수정:** Step 1에서는 baseline 일치를 위해 legacy Damage 호출을 허용하되, `DamageBlock` 자체의
     public 실행 계약은 `SkillContext`를 받도록 고정한다. Step 3 damage result API 전환 전까지 direct
     BattleField 조회가 legacy Damage 내부에 남는 것을 명시적 예외로 둔다.

4. **[P3] `SkillDefinition` v0 저장 스키마의 기본값/검증 실패 정책이 구현자에게 조금 더 필요하다**
   - Step 1은 `.tres` round-trip을 요구하지만, null/empty `Id`, null effect, range 음수, definition 누락 시
     `TurnAction_Skill`이 Failure인지 fail-fast인지가 아직 느슨하다.
   - **권장:** Step 1 v0는 authoring/editor validation까지 만들지 않고 runtime guard를 둔다. definition
     누락, empty id, range < 0, null effect는 SCRIPT ERROR 없이 `ActionState.End` 또는 BT Failure로 닫고,
     이 정책을 테스트에 포함한다.

## Open Decisions

- **Step 1 state seam 형태:** 권장 선택은 legacy 3종을 깨지 않는 최소 compatibility seam이다. 구현 시점에
  `TurnActionBase`에 state hook을 추가할지, `CharacterArticle`에 현재 실행 handle을 추가할지 결정하되,
  완료 조건은 "동일 `SkillDefinition`/`TurnAction_Skill` Resource를 두 유닛이 공유해도 phase queue와
  used cost가 섞이지 않는다"로 고정한다.
- **Failure 반환 정책:** definition/config 오류를 BT Failure로 넘길지, no-op Success로 넘길지 Step 1 전에
  하나로 고정한다. 전술 행 fallback을 생각하면 Failure가 더 자연스럽지만, 현재 legacy TurnAction은 대상이
  없어도 Success로 끝나는 경향이 있어 기존 전투 흐름과 대조가 필요하다.

## Step Assessment

- **Step 0:** 완료 가능. 실제 코드 대조 결과 위 설계 보완을 Task/ADR에 반영하면 구현 승인 가능하다.
- **Step 1:** 범위에 compatibility seam을 추가해야 한다. 이 추가 없이는 SkillState isolation 완료 조건을
  코드로 만족시키기 어렵다. DamageBlock/Slash Data 자체는 좋은 첫 기준이다.
- **Step 2:** windup 중 BT 재평가 정지와 "영창 중" 플래그 노출은 현재 `CurrentTurnAction` 재개 경로와 잘
  맞는다. 단, Step 1 state seam 위에 얹혀야 한다.
- **Step 3:** Mana와 hit chance를 한 Step에 넣는 것은 적절하지만, damage result API 전환이 커질 수 있다.
  Step 3 시작 전 Step 1/2 report shape를 너무 크게 고정하지 않는 편이 좋다.
- **Step 4:** 스턴 취소와 턴 순환 회귀는 위험이 크므로 별도 sub-step으로 쪼개도 좋다. 특히 현재 턴 유닛
  조작 시 `GetNextTurnArticle` 리셋 위험은 반드시 fixture가 필요하다.
- **Step 5~6:** ChainBlock과 12종 데이터 입력은 앞 단계의 block/report/selector 계약이 안정된 뒤 진행하는
  순서가 타당하다.

## Verification Assessment

필수 자동 검증:

- `SkillDefinition`/`EffectBlock` `.tres` 저장 -> 재로드 round-trip.
- 같은 `SkillDefinition`과 같은 `TurnAction_Skill` Resource를 두 유닛/두 BT 노드가 공유하는 상태 격리.
- 기존 `TurnAction_Attack` vs 데이터 기반 `베기`: phase 지점, RNG 소비, 크리티컬, 최종 HP 일치.
- invalid definition/effect/range 실패 정책: SCRIPT ERROR 0, 상태 불변.
- CB-001 `cb001_step1~4` 결정론 회귀 유지.
- Godot headless `--import` 0 parse error.

## Verdict

**Approved after design fixes** — 데이터 기반 스킬 방향, combo 보류, 기존 BT 경로 유지, DamageBlock부터
도입하는 단계 구성은 타당하다. 다만 Step 1 구현 전에 `SkillState`가 실제로 어디에 저장되고 어떻게
Running 재개에 복원되는지 legacy TurnAction 계약 보완을 문서화해야 한다. 해당 보완을 Task/ADR에 반영하면
Step 1 구현으로 진행 가능하다.

---

# SK-001 Step 1 Code Review

Step 1(골격 + DamageBlock + Slash 데이터) 구현 코드 리뷰다. 구현 보고를 신뢰하지 않고
`TurnAction_Skill`/`SkillState`/`DamageBlock`과 실제 BT 실행 경로(`CharacterArticle.TurnPlay`,
`BehaviorTree_TurnAction.PerformAction`), legacy `TurnAction_Attack` 기준선, Step 1 테스트를 대조했다.

## 발견 사항

P0/P1 없음. 아래는 모두 P2/P3다.

1. **[P2] `SkillContext` 주입 seam이 시그니처뿐 — 유일한 block이 `context`를 전혀 사용하지 않음**
   - 발생 조건: 모든 `DamageBlock` 실행.
   - 사용자 영향: 없음(동작 정상). 단 "block은 `SkillContext`로만 세계를 본다"는 ADR 목표가 실증되지
     않은 채 남는다. `SkillContext.CombatRandRange` 두 오버로드와 `FxPlayer`/`TileMapLayer`가 dead code다.
     block이 더 쌓이면 seam이 검증 없이 굳는다.
   - 파일/라인: `Assets/Script/SkillSystem/Blocks/DamageBlock.cs:14`(context 미사용),
     `Assets/Script/SkillSystem/SkillContext.cs:27`(미사용 RNG gateway).
   - 권장 수정 방향: Step 0에서 legacy `Damage.CreateDamage` 내부 전역 조회는 명시적 예외로 허용됐으므로
     Step 1 판정을 막지는 않는다. 다만 Step 3 damage result API 전환 시 최소한 RNG를
     `context.CombatRandRange`로 태워 seam을 실제로 사용하게 만든다.

2. **[P2] `CharacterArticle.CurrentTurnActionState`가 `object`라 타입 안전성이 약함**
   - 발생 조건: 실행 상태 저장/복원 전 구간.
   - 사용자 영향: 런타임 무해하나 `is SkillState` 패턴 캐스팅이 4~5곳에 흩어져 유지보수 위험. 저자도
     위험으로 인지한 compatibility seam.
   - 파일/라인: `Assets/Script/Article/CharacterArticle.cs:16`,
     `Assets/Script/SkillSystem/TurnAction_Skill.cs:47,55,105,123`.
   - 권장 수정 방향: `TurnAction` 네임스페이스(이미 Article이 의존)에 `ITurnActionState` 마커 인터페이스를
     두고 `SkillState`가 구현하게 하면 SkillSystem 역의존 없이 타입 안전성을 얻는다.

3. **[P3] `DamageBlock`의 magical 분기가 Step 1에서 도달 불가한 미검증 코드이고 physical과 의미가 비대칭**
   - 발생 조건: `DamageType == Magical`. slash는 physical이라 Step 1에서 실행/테스트되지 않음.
   - 사용자 영향: 잠재. physical은 `Min~Max` 롤, magical은 `Min` 무시 고정 `Max`(`MaxDamage,MaxDamage`)라
     같은 블록에서 의미가 갈리는데 문서/주석이 없다. legacy `TurnAction_MagicBolt`의 flat-max 재현이라
     버그는 아니나 designer가 `MinDamage`를 설정하면 조용히 무시된다.
   - 파일/라인: `Assets/Script/SkillSystem/Blocks/DamageBlock.cs:29`,
     기준: `Assets/Script/TurnAction/Skill/Magic/TurnAction_MagicBolt.cs`.
   - 권장 수정 방향: Step 2 MagicBolt 이관 시점으로 미루거나(현재 분기 제거), 남긴다면 "magical=고정 최대"
     의도를 주석으로 명시한다.

4. **[P3] 미지원 selector/target side가 fail-closed가 아니라 "턴 소모 no-op"**
   - 발생 조건: `TargetSide != Enemy` 또는 `TargetSelectorDefault != Nearest`인 definition.
   - 사용자 영향: 잠재. `SelectTarget`이 `null`을 반환해도 Init은 state 생성 + phase enqueue를 진행해
     대상 0으로 턴만 소모하고 Success로 닫힌다. `IsValidV0`는 selector 지원 여부를 검증하지 않는다.
     slash엔 무해.
   - 파일/라인: `Assets/Script/SkillSystem/TurnAction_Skill.cs:130`,
     `Assets/Script/SkillSystem/SkillDefinition.cs:20`.
   - 권장 수정 방향: Step 2 selector/side 필터 도입 시 미지원·무대상 경로 정책을 fail-closed로 고정하고
     테스트에 남긴다.

5. **[P3] 미사용 scaffolding·스타일 잔여물**
   - `SkillState.Reports`는 채워지지만 소비/단언되는 곳이 없다(report sink는 Step 3). 미사용 using
     `...TurnAction.Skill`(`TurnAction_Skill.cs:6`). dequeue 스타일 불일치(Start/RunAnimation은 owner
     패턴 매치, DamageBlock은 캡처한 `state`). base `TurnActionBase.MasterCost`/`_usedCost`/`Cost`는 skill이
     `Action()`을 전면 override해 dead — Step 2 windup의 cost 모델(현재 `MarkCostUsed`는 큐 drain 시 1회만
     증가)에서 재점검 필요.
   - 권장 수정 방향: 별도 정리. Step 2 착수 시 함께 처리 가능.

## 잘 된 부분

- 테스트가 실제 런타임 경로를 정확히 대표한다. `CharacterArticle.TurnPlay:47`이 `CurrentTurnAction != null`
  이면 tree를 behave하지 않고 `Action()`만 구동하므로 `Init`은 액션 선택 시 1회만 호출된다. 테스트의
  "Init 1회 + Action 루프"(`Sk001Step1SkillSystemTest.cs:283`)가 이와 일치한다.
- legacy Attack과 신규 slash가 상태열 `[Running,Running,Running,End]`, RNG 1회 소비, 종료 anim=Idle로
  동일하게 수렴함을 트레이스로 확인했다. baseline 문자열이 `next_rng`까지 포함해 RNG 스트림 위치 회귀를
  잡는다.
- 공유 Resource에 mutable 실행 상태가 없다. phase 메서드가 인스턴스 메서드지만 `owner.CurrentTurnActionState`
  만 조작하고 `DamageBlock`은 per-call `state` 클로저를 쓴다. Test C(격리)가 잘 커버한다.
- base `Init/Finish/Action` virtual 개방은 legacy 3종(OnInit/OnFinish/ActionExecute 훅)에 무영향인 additive
  변경이다.

## 검증 결과

- 통과(코드 트레이스/구조 확인): baseline 상태열·RNG·anim 등가, SkillState 격리, `.tres` round-trip 경로,
  invalid definition fail-closed(`no state` + `Action -> End`), virtual seam 안전성.
- 실행하지 못한 항목: 이 리뷰 세션에서 `sk001_step1_skill_system_test.tscn` 및 CB-001 회귀를 직접 재실행하지
  않았다(구현 보고의 ALL PASS를 코드로 교차 검증). 필요 시 headless 재실행으로 보강 가능.

## 판정

**수정 후 완료** — 완료 조건(baseline 일치, SkillState 격리, round-trip, fail-closed)을 코드상 충족하고
P0/P1이 없다. P2 2건(context seam 실증, `object` 타입)은 Step 2 착수 전 정리를 권하며, P3는 별도 정리 가능.
다음 Step은 현재 Step의 미완성 부분에 의존하지 않는다.


## Step 1 Fix Follow-up

판정: **Approved (minor fixes applied)**.

확인한 핵심 안전성:

- `BehaviorTree_TurnAction.PerformAction()`은 액션 선택 시 `TurnAction.Init(this)`를 호출하지만, Running 이후에는 `CharacterArticle.TurnPlay()`가 `CurrentTurnAction.Action(...)`을 직접 구동하므로 `TurnAction_Skill.Init()`이 매 frame 재호출되지 않는다. Step 1 테스트의 `Init 1회 + Action loop`가 실제 경로를 대표한다.
- 기존 Attack과 데이터 기반 `slash`는 headless baseline에서 `[Running, Running, Running, End]`, HP 변화, 다음 RNG 시퀀스, 종료 animation `Idle`이 일치한다.
- `SkillState`는 유닛별 일반 C# 객체이고, `SkillDefinition`/`EffectBlock`/`TurnAction_Skill` Resource에는 실행 중 phase queue/used cost/target이 저장되지 않는다. 공유 Resource 격리는 `sk001_step1_skill_system_test` C 케이스로 검증했다.
- `TurnActionBase.Init`/`Finish`/`Action` virtual seam은 additive이며 legacy 3종 TurnAction의 base hook 경로를 유지한다.

리뷰 후 반영한 minor fixes:

- `CharacterArticle.CurrentTurnActionState`를 `object`에서 `ITurnActionState` marker interface로 좁혔다. `SkillState`가 이를 구현한다.
- Step 1 v0에서 지원하지 않는 `TargetSide != Enemy` 또는 `TargetSelectorDefault != Nearest`를 `SkillDefinition.IsValidV0()`에서 fail-closed 처리한다. 테스트 D에 unsupported side no-state/HP 불변 단언을 추가했다.
- `DamageBlock` magical 분기에 legacy MagicBolt parity(고정 `MaxDamage`) 주석을 추가했다.
- `TurnAction_Skill`의 `using AutoCrawler.Assets.Script.TurnAction.Skill`은 `OrderByCanonical` extension에 필요하므로 유지한다.

남은 권고/후속:

- `DamageBlock`은 Step 1 기준선 일치를 위해 아직 legacy `Damage.CreateDamage<T>()`를 호출하고, legacy Damage 내부가 `BattleFieldScene.BattleField.TurnHelper`를 직접 조회한다. `SkillContext.CombatRandRange` seam 실증은 Step 3 damage result API 전환 시 처리한다.
- `SkillState.Reports`는 Step 1에서 채워지지만 소비자는 없다. report sink가 붙는 Step 3에서 단언을 추가한다.
- Step 2 windup에서 cost 모델을 재검토한다. 현재 Step 1은 큐 drain 시 act cost 1회를 소비하는 slash baseline에 맞춰져 있다.
- Godot headless 종료 시 ObjectDB/resource leak warning과 CB-001 Step4의 기존 animation 누락 ERROR 로그는 남아 있다. 테스트 판정은 전부 PASS다.

검증:

- `dotnet build AutoCrawler.sln -c Debug` — 경고 0, 오류 0.
- `sk001_step1_skill_system_test.tscn` — ALL PASS(unsupported side fail-closed 포함).
- CB-001 Step 1~4 — ALL PASS.
- `godot --headless --path . --import` — exit 0, parse/import 실패 없음. 기존 editor layout/resource 종료 경고는 출력됨.

# SK-001 Step 2 Code Review

Step 2(Windup + MagicBolt 이관 fixture + TargetSelector) 구현 리뷰다. 구현 보고를 신뢰하지 않고
`TurnAction_Skill`(재작성된 `Action`/`SelectTarget`/`CastingPhase`), `SkillState`, `SkillDefinition`,
`magicbolt.tres`, legacy `TurnAction_MagicBolt`/`TurnAction_Cast` 기준선, `SkillUtil.OrderByCanonical`,
Step 2 테스트를 대조하고 headless로 실행했다.

## 발견 사항

P0/P1 없음.

1. **[P2] LowestHp/HighestHp 동점 처리가 ADR-017 정규 순서가 아니라 유클리드 거리를 사용 — 수정함**
   - 발생 조건: `TargetSelectorDefault == LowestHp` 또는 `HighestHp`이고 최우선 정렬(HP)이 동점인 대상이
     둘 이상일 때.
   - 사용자 영향: 동점자 선택이 ADR-017 정규 순서(Manhattan 거리 -> Y -> X)와 갈린다. 예) 시전자 (5,5),
     후보 (5,8)은 Manhattan 3/유클리드² 9, 후보 (7,7)은 Manhattan 4/유클리드² 8. 정규 순서는 (5,8)을,
     기존 유클리드 코드는 (7,7)을 골라 결정론 회귀를 만든다.
   - 파일/라인: `Assets/Script/SkillSystem/TurnAction_Skill.cs`의 구 `SelectTarget`
     (`.ThenBy(t => (t.TilePosition - caster.TilePosition).LengthSquared())`).
   - 수정: 동점 처리를 `SkillUtil.ThenByCanonical`로 교체해 `OrderByCanonical`과 동일한 Manhattan 정규
     순서를 공유한다. 유클리드/Manhattan이 갈리는 fixture(`A.TieCanonical`)를 테스트에 추가했다.

2. **[P2] "영창 중" 플래그가 구체 타입 `SkillState`로만 접근 가능 — 수정함**
   - 발생 조건: 완료 조건 6("영창 중 플래그가 조건 어휘/HUD/영창 취소가 소비 가능한 형태로 외부 노출")
     대비, `IsCasting`이 `SkillState`에만 존재하고 `CurrentTurnActionState`는 빈 marker `ITurnActionState`
     였다. 외부 소비자가 SkillSystem 구체 타입에 하드 의존해야 소비 가능했다.
   - 파일/라인: `Assets/Script/TurnAction/ITurnActionState.cs`, `Assets/Script/Article/CharacterArticle.cs`.
   - 수정: `ITurnActionState.IsCasting` getter를 도입(`SkillState`가 이미 구현)하고,
     `CharacterArticle.IsCasting => CurrentTurnActionState?.IsCasting ?? false` 창구를 추가해 구체 타입
     의존 없이 소비 가능하게 했다. 실제 소비처(영창 취소)는 Step 4 범위로 남는다.

3. **[P3] `SkillTargetSelector.Self`가 미처리로 조용히 Nearest로 동작 — 수정함**
   - 발생 조건: `TargetSelectorDefault == Self`(현재 데이터엔 없음). `SelectTarget`의 분기가 이를 처리하지
     않아 default `OrderByCanonical`(=Nearest)로 흘렀다. 자기 대상이 아니라 최근접 적/아군을 잘못 확정한다.
   - 파일/라인: `Assets/Script/SkillSystem/TurnAction_Skill.cs` `SelectTarget`.
   - 수정: `TargetSide == Self || TargetSelectorDefault == Self`이면 사거리/필터를 우회해 시전자를 확정하도록
     통합했다.

## 잘 된 부분

- windup 재개 계약이 정확하다. `CastingPhase`가 애니메이션 완료 시 `Executed`(End 아님)를 반환하고
  `Action`이 `CurrentTurnActionState`를 비우지 않으므로, `CharacterArticle.TurnPlay`가 다음 턴에
  `CurrentTurnAction.Action`을 직접 구동(BT `Behave` 우회)해 남은 phase를 재개한다. 이는 legacy
  `TurnAction_Cast`의 턴 소비 구조와 등가다.
- `MasterCost = ActCost + WindupCost`(magicbolt=2)가 legacy `_exportCost=2`와 일치하고, 직접 Action 루프
  기준선(`[Running, Executed, End]`, hp -20, anim=Idle)이 legacy MagicBolt와 동일하다. `MagicalDamage`는
  `IsCritical=false`에 RNG 미소비라 magicbolt 경로는 RNG 스트림 회귀가 없어 상태/HP 비교로 충분하다.
- 실행 상태는 유닛별 `SkillState`에만 있고 공유 Resource(`SkillDefinition`/`EffectBlock`/`TurnAction_Skill`)
  에 저장되지 않는다.

## 검증 결과

- `dotnet build AutoCrawler.sln -c Debug` — 경고 0, 오류 0.
- `sk001_step2_windup_test.tscn` — ALL PASS(추가한 `A.TieCanonical` 포함). `A.TieCanonical`은 수정 전
  유클리드 코드에서 실패, 수정 후 통과를 확인해 회귀 방지 효과를 검증했다.
- `sk001_step1_skill_system_test`, CB-001 Step 1~4 — 전부 ALL PASS(인터페이스/`CharacterArticle`/
  `TurnAction_Skill` 변경 후 회귀 없음).
- `godot --headless --path . --import` — exit 0. 기존 editor layout/resource 종료 경고만 출력.

## 남은 위험/후속

- 사거리 내 대상이 없을 때 `SelectTarget`이 `null`이어도 `Init`이 windup/effect phase를 enqueue해 턴만
  소모하는 no-op이 남는다(legacy MagicBolt의 `_targetPosition` 기본값 거동과 동형). 무대상 fail-closed는
  Step 3 지불 게이트/실패 신호와 함께 정리하는 편이 자연스럽다.
- `IsCasting`의 실제 소비처(스턴 영창 취소)는 Step 4 범위다.
- Test C의 인터리브는 단일 caster 재개만 확인한다. 실제 다른 유닛 턴 삽입 회귀는 Step 4 턴 순환 fixture와
  함께 강화 가능하다.

## 판정

**수정 후 완료** — Step 2 완료 조건(selector/side, 동점=ADR-017 정규 순서, MasterCost=windup+act,
windup 중 BT 우회/재개, 영창 중 플래그 외부 노출, MagicBolt 기준선 재현)을 코드상 충족하고 P0/P1이 없다.
리뷰 중 발견한 P2 2건·P3 1건을 이 세션에서 수정·재검증했다. 다음 Step은 현재 Step의 미완성 부분에
의존하지 않는다.

# SK-001 Step 3 Code Review

Step 3(Mana + Hit Chance Payment Gate) 구현 리뷰다. `Mana`, `ActionState.Failure` 전파
(`BehaviorTree_TurnAction.PerformAction`/`CharacterArticle.TurnPlay`/`BehaviorTree_Selector`),
`TurnAction_Skill`의 마나 게이트, `DamageBlock`의 명중 판정/report/RNG 순서를 실행 경로까지 대조하고
headless로 실행했다.

## 발견 사항

P0/P1/P2/P3 없음(제품 코드). 아래는 확인한 안전성과 설계 판단이다.

- **지불 실패 신호가 실제 BT fallback을 낸다.** `BehaviorTree_Selector.OnBehave`는 `Failure`인 child에서
  다음 child로 넘어간다. `PerformAction`이 `ActionState.Failure`를 `BtStatus.Failure`로 매핑하고
  CurrentTurnAction을 세우지 않으므로, 마나 부족 스킬은 턴 손실 없이 다음 행으로 fallback된다. Test D가
  `PerformAction`(Behave 경유)과 `TurnPlay`(재개 경로) 양쪽 매핑을 검증한다.
- **baseline RNG 스트림 보존.** `HitChance == 100`(기본값)은 hit roll을 생략한다. slash/magicbolt는 모두
  ManaCost 0·HitChance 100이라 Step 1(next_rng 포함)·Step 2 baseline이 그대로 유지됐다(회귀 ALL PASS).
- **context seam 실증.** Step 1/2에서 미사용이던 `SkillContext.CombatRandRange`를 명중 판정이 실제로
  1회 소비한다. RNG 순서 `명중 -> 크리티컬 -> 피해`는 단일 `TurnHelper._combatRng` 위에서 결정론적이다.
- **지불은 소모 없이 검사 우선.** `Mana.TrySpend`는 부족 시 상태를 바꾸지 않고 false. Test A가 마나/HP/RNG
  무변경을 각각 단언한다.

## 잘 된 부분

- `ActionState.Failure`는 additive라 legacy 3종 TurnAction(Failure 미반환)에 무영향이다. `ActionState`
  사용처가 전부 `is`/`==`라 exhaustive switch 파손이 없다.
- 피해 적용을 `DamageBlock.ApplyDamage`로 분리해 legacy Damage/UI 결합을 한 지점에 격리했다(migration 경계
  문서화, done condition #4).
- report sink(`SkillState.Reports`)가 Step 3에서 처음으로 소비/단언된다(miss/damage 태그).

## 검증 결과

- `dotnet build` 경고/오류 0.
- `sk001_step3_mana_hit_test` — ALL PASS(20). 마나 게이트(부족→Failure·무변경, 충분→지불·피해),
  HitChance 0/100 miss/hit + report, miss가 CombatRng 정확히 1회 소비, 부분 명중 seed 재현,
  `PerformAction`/`TurnPlay` Bt Failure.
- `sk001_step1`·`sk001_step2`·CB-001 Step 1~4 — ALL PASS(회귀 없음).
- `--import` exit 0, `Mana` GlobalClass 등록.
- 구현 중 발견한 것은 테스트 결함 1건(다중 상대 미격리)뿐이며 제품 결함이 아니다. 격리+방어력 0으로 수정.

## 판정

**완료** — Step 3 완료 조건(마나 부족 무변경 Failure, 명중=CombatRng only·재현, miss/hit report,
damage result migration 지점 문서화)을 코드상 충족하고 P0/P1이 없다. 남은 후속(Mana의 production scene
배선, 무대상 fail-closed/환불, legacy Damage/UI 결합 제거)은 다음 Step을 막지 않는다.

# SK-001 Step 4a Code Review

Step 4a(Status/Control Blocks + 스턴 영창 취소 + 턴 순환) 구현 리뷰다. KnockbackBlock은 사용자 승인으로
Step 4b 분리. `StatusController`, `Damage` 배율 hook, `CharacterArticle.TurnPlay`/`ApplyTurnStartEffects`,
이동 노드 bind 가드, 4종 블록을 실행 경로까지 대조하고 headless로 실행했다.

## 발견 사항

P0/P1/P2/P3 없음(제품 코드). 아래는 확인한 안전성과 설계 판단이다.

- **제어 상태를 StatusController 카운터로 표현한 것은 정당하다.** 기존 `StatusAffect`의 `IAffectedUntilTheEnd`는
  `Apply`에서 `_usedCost==0` 최초 tick에만 `ApplyAffect`하고 같은 tick에 `Cost<=0`이면 `UnapplyAffect`한다.
  즉 MasterCost=1 제어는 적용과 만료가 한 tick에 붕괴한다. 카운터 모델(`OnTurnStart` 틱 + `ConsumeStunForTurn`
  소비)은 이 붕괴를 피하고 frame 비종속이다.
- **baseline 보존.** 배율 기본 1.0을 롤 이후 곱하므로(`(int)(roll*(crit?2:1)*mult)`) RNG 스트림이 이동하지 않고,
  int 정밀도 안에서 mult=1.0은 항등이다. `OnTurnStart`/bind/stun 가드는 상태가 없으면 no-op다. slash/magicbolt·
  legacy 피해·CB-001 결정론 회귀가 전부 유지됐다(재실행 ALL PASS).
- **스턴 영창 취소가 이중으로 안전하다.** StunBlock 적용 시 `IsCasting` 대상을 `CancelCasting`(환불 없음)하고,
  TurnPlay 스킵에서도 `CancelCasting`을 호출한다(idempotent). 지불된 마나/비용은 되돌리지 않는다(E.ManaNotRefunded).
- **턴 순환 리셋 위험이 재현되지 않는다.** `CancelCasting`은 `_turnAffectedArticleList`에서 유닛을 제거하지 않으므로
  `GetNextTurnArticle`의 `IndexOf`가 -1이 되지 않는다. 현재 턴 유닛을 외부에서 바꿔도 다음 턴이 리스트 처음이 아니라
  Priority->SpawnIndex 후속임을 F가 회귀로 고정한다.

## 잘 된 부분

- `ArticleBase`의 virtual 배율 + `CharacterArticle` 오버라이드로 `Damage`가 giver/recipient를 균일하게 조회한다
  (SkillSystem 역의존 없음). legacy Damage도 같은 hook을 타되 기본 1.0이라 무변경.
- 블록 phase가 DamageBlock과 동일한 패턴(enqueue→dequeue→Executed)이라 실행기 계약이 일관된다.
- 컨트롤러 순수 로직을 씬 없이 [G]로 분리해 apply/만료/재진입 수학을 frame 비종속으로 못 박았다.

## 검증 결과

- `dotnet build` 경고/오류 0.
- `sk001_step4_status_control_test` — ALL PASS(39, [G]/[A]~[F]).
- `sk001_step1`~`step3`·CB-001 Step 1~4 — ALL PASS(회귀 없음).
- `--import` exit 0, 4종 블록 GlobalClass 등록.
- 구현 중 발견한 것은 테스트 결함 0건(Step 3의 격리 헬퍼를 재사용). 제품 결함 없음.

## 판정

**완료** — Step 4a 완료 조건(4종 블록 적용/만료/재진입, 턴 시작/effect phase 한정·frame 비종속, 스턴 영창
취소·비용 미환불, 턴 순환 무교란)을 코드상 충족하고 P0/P1이 없다. `KnockbackBlock`(Step 4b)과 데이터 배선은
다음 작업을 막지 않는다.

## Step 4a Fix Follow-up

외부 리뷰(판정: Approved, minor fixes recommended)의 P2/P3를 수정·재검증했다.

- **[P2] 수정** — `ApplyDamageDealtBuff`/`ApplyDamageTakenDebuff`가 `turns <= 0`이면 배율을 바꾸지 않고
  early-return한다. 기존엔 배율을 먼저 설정하고 카운터를 0으로 둬 `OnTurnStart` 만료 분기(`if (_turns > 0)`)를
  못 타서 영구 배율이 됐다. 잘못된 `.tres`(turns 0/음수) 한 번으로 영구 버프가 되는 위험 제거. 회귀 방지:
  `G.Dealt_zeroTurns_noop`, `G.Taken_negTurns_noop` 추가.
- **[P3] 수정** — `TestStunCancelsCasting`이 windup 첫 프레임을 만들려 `Action`을 호출하며 fixture에 없는
  "Cast" 애니메이션을 재생해 headless ERROR가 났다. `WindupCost>0`이면 `Init`만으로 `IsCasting=true`가
  되므로 `Action` 호출을 제거했다. 제품 코드 변경 없이 테스트만 수정. 재실행 시 비-benign ERROR 로그 없음.

검증: `dotnet build` 경고/오류 0. `sk001_step4_status_control_test` ALL PASS(41). `sk001_step1~3`·CB-001
Step 1~4 회귀 ALL PASS.

# SK-001 Step 4b Code Review

Step 4b(`KnockbackBlock`) 구현 리뷰다. `KnockbackBlock`과 tilemap 점유 파이프라인(`BattleFieldTileMapLayer`
`OnArticleMove`/`UpdateAStar`, `ArticleBase.TilePosition` `OnMove`)을 대조하고 headless로 실행했다.

## 발견 사항

P0/P1/P2/P3 없음. 아래는 확인한 안전성이다.

- **점유/AStar 상태 중복이 없다.** `KnockbackBlock`은 최종 위치를 `TilePosition`으로 한 번만 설정한다.
  `ArticleBase.TilePosition` 세터가 `OnMove(from,to)`를 발행하고 tilemap의 `OnArticleMove`가
  `_placedArticles[from]=null; _placedArticles[to]=article`로 갱신하며, `UpdateAStar`가 `_placedArticles`에서
  solid를 재구성한다. 넉백은 이 파이프라인을 재사용할 뿐 별도 점유 테이블을 만들지 않는다. A.Occupancy*/
  A.AStar*가 실측한다.
- **경계/충돌 검사가 이동 노드와 일관된다.** `GetUsedRect().HasPoint`(경계), `GetArticle(next) != null`(점유)로
  한 칸씩 전진하며 마지막 유효 칸에서 멈춘다. `BehaviorTree_Move`/`MultipleMove`의 경계·solid 검사와 같은 API.
- **방향이 4방향 카디널로 고정된다.** 시전자→대상 벡터의 더 큰 축(동률 X)을 택해 대각 입력도 카디널이 된다
  (그리드 이동 모델과 일치). 동일 칸/자기 대상은 dir=0으로 no-op(knockback:0).
- **OnMove 단일 발행.** `GlobalPosition`(시각)은 신호를 발행하지 않고 `TilePosition` 1회 설정만 `OnMove`를
  발행하므로 from=시작·to=최종으로 정확히 한 번 발행된다(A.OnMove*).

## 검증 결과

- `dotnet build` 경고/오류 0.
- `sk001_step4b_knockback_test` — ALL PASS(17, [A]~[D]).
- `sk001_step1~4`·CB-001 Step 3~4 — ALL PASS(회귀 없음).
- `--import` exit 0, `KnockbackBlock` GlobalClass 등록.
- 구현 중 발견한 것은 테스트 결함 1건(폭 좁은 맵에서 중앙 기준 +2 수평 밀기가 경계 초과 → 좌상단 모서리
  기준 1칸 밀기로 수정). 제품 결함 아님.

## 판정

**완료** — Step 4b 완료 조건(점유 충돌, 맵 경계, 이동 중단 위치, `OnMove` 발행, AStar/tilemap 점유 갱신)을
별도 fixture로 충족하고 P0/P1이 없다. Step 4(4a+4b) 전체 완료. 다음은 Step 5 ChainBlock.

## Step 4b Fix Follow-up

외부 리뷰의 **[P2] 테스트 fixture** 지적을 수정했다(제품 코드 무변경).

- 문제: 격리 헬퍼 `IsolateOtherOpponents`가 미사용 적을 (900,900) 맵 밖 `TilePosition`으로 옮겼다.
  세터가 `OnMove`를 발행 → tilemap이 맵 밖 좌표를 `_placedArticles`에 저장 → 테스트 A의 `UpdateAStar`가
  `SetPointSolid((900,900))`를 호출해 `Point out of bounds` ERROR(assertion은 PASS지만 non-benign 로그).
- 수정: `ParkUnusedOpponents`로 교체. `GetUsedRect()` 안에서 미점유·사거리 밖(우선) 칸을 찾아 배치하고
  경로 칸을 예약해 충돌을 막는다. 대상은 항상 시전자 인접(dist 1)에 둬 `SelectTarget` 확정을 고정한다.
- 제품 코드(bounds guard 추가) 대신 테스트를 유효 좌표로 고쳤다(리뷰 권고와 일치).
- 재검증: `sk001_step4b_knockback_test` 17 assertions ALL PASS, out-of-bounds/non-benign ERROR 없음.
  `sk001_step1~4`·CB-001 회귀 ALL PASS.

참고: `sk001_step3`/`sk001_step4`의 격리는 아직 (900,900)을 쓰지만 그 테스트들은 `UpdateAStar`를 호출하지
않아 ERROR가 없다. 향후 그 테스트에 AStar 검사를 추가하면 같은 방식으로 in-bounds 격리로 바꿔야 한다.

# SK-001 Step 5 Code Review

Step 5(`ChainBlock` 이관) 구현 리뷰다. `ChainBlock`과 legacy `TurnAction_ChainLightning`의 대상 선택/피해를
대조하고, 동일 레이아웃에서 두 구현의 결과를 headless로 비교했다.

## 발견 사항

P0/P1/P2/P3 없음. 아래는 확인한 등가성이다.

- **대상 선택이 legacy와 코드상 동일하다.** `ChainBlock.GetChainTarget`은 `TurnAction_ChainLightning.
  GetChainTarget`과 같은 필터(`IsAlive`·`TilePosition != tilePosition`·`IsOpponent(caster)`)와 정렬
  (`OrderBy(hit.Contains)` → `ThenByCanonical`)을 쓴다. 첫 대상도 `SelectTarget`(Enemy·Nearest·Range3) =
  legacy `GetTarget`이라 같은 opp에서 시작한다.
- **RNG 무소비·고정 피해로 baseline이 결과로 검증된다.** 연쇄는 `MagicalDamage`(IsCritical=false)라 RNG를
  건드리지 않고 피해가 고정이므로, 프레임 타이밍/FX와 무관하게 대상별 HP가 결정적이다. A.Baseline이 legacy와
  block의 대상별 HP가 완전히 같음을(`...->468` ×3, opp4 무변경) 실측했다.
- **단일 phase 설계가 정당하다.** legacy의 Cast/Ignition/Discharge 다중 프레임은 애니메이션/FX 진행일 뿐
  권위 상태(HP)를 만드는 건 Ignition의 damage뿐이다. ChainBlock은 그 damage를 DamageBlock과 같은 단일
  phase에서 적용해 같은 결과를 낸다(ADR-018 §4).
- **미타격 우선이 canonical을 이긴다.** opp2 홉에서 이미 맞은 opp1(canonical Y로는 우선)이 아니라 미타격
  opp3가 선택됨을 B.Hop2가 못박는다.

## 검증 결과

- `dotnet build` 경고/오류 0.
- `sk001_step5_chain_test` — ALL PASS(A.Baseline + B 체인 선택), non-benign ERROR 없음(ChainLightning FX
  헤드리스 정상 구동).
- `sk001_step1~4b`·CB-001 Step 1~4 — ALL PASS.
- `--import` exit 0, `ChainBlock` GlobalClass 등록.

## 판정

**완료** — Step 5 완료 조건(체인 선택 규칙 유지, seed 기준선 일치, 삭제 전 참조 대체 가능성 확인)을 충족하고
P0/P1이 없다. `TurnAction_ChainLightning` 삭제와 데이터 배선은 Step 6. 다음은 Step 6 Phase1 데이터 12종.

# SK-001 Step 6 Code Review

Step 6(Phase1 12종 데이터 팩) 구현 리뷰다. 블록 확장(`StunBlock.Chance`, `SelfBuffBlock.Kind`),
12종 `.tres`, 생성/검증/실행 테스트를 대조하고 headless로 실행했다. SK-001 마지막 Step.

## 발견 사항

P0/P1/P2/P3 없음. 아래는 확인한 안전성과 설계 판단이다.

- **블록 확장이 기존 baseline을 보존한다.** `StunBlock.Chance`는 `Chance < 100`일 때만 CombatRng를 1회
  소비한다(기본 100 → roll 없음). `SelfBuffBlock.Kind`는 기본 `DamageDealt`라 Step 4a 동작과 동일. 두 확장
  모두 Step 4a 테스트(기본값 사용)를 깨지 않는다(회귀 ALL PASS).
- **데이터 팩이 로드/검증을 통과한다.** 12종 모두 `IsValidV0()` + `Effects>0`. 직렬화 round-trip은 커밋
  파일이 아니라 스크래치(`user://`)에서 검증해 designer 편집과 분리했고 `HitChance`/`Chance`까지 보존됨을
  확인했다. 생성은 "없을 때만"(부트스트랩)이라 재실행이 커밋 데이터를 덮어쓰지 않는다.
- **4 카테고리가 실제로 실행된다.** 기본기(베기 피해), 강기(강타 피해+넉백1), 제어(금강체 받는피해 0.5·
  속박 3턴), 흡수(마나 흡수 대상 −16·시전자 +16), StunBlock Chance 0/100 결정론, 연쇄 뇌격 3체를 실측.
- **지불 게이트가 데이터에서도 작동한다.** 초기 실행 실패(강타/금강체/속박)는 fixture 캐스터에 Mana 풀이
  없어 Step 3 지불 게이트가 정상 fail-closed한 결과였다. 캐스터에 마나를 주입하자 실행됐다(테스트 결함,
  제품 정상).

## 의도적 근사(수치표 대비)

- 조준 사격 "크리 +15%p": DamageBlock에 크리 보너스 없음 → 고위력+명중100 windup으로 근사(후속).
- 연쇄 뇌격 "명중 90": ChainBlock은 per-hit 명중 없음(legacy ChainLightning parity).
- 마나 흡수 "준 피해 50% 회복": 고정 `ManaDrain(16)` 대상 흡수로 근사(정확한 %는 damage-result API 후속).
- 마법 min 미반영(마탄/연쇄뇌격 max only): D-2 공식 준수. `combo`: 필드/데이터 없음.

## 검증 결과

- `dotnet build` 경고/오류 0.
- `sk001_step6_data_pack_test` — ALL PASS(A 생성·검증·round-trip / B~D 실행). non-benign ERROR 없음.
- `sk001_step1~5`·CB-001 Step 1~4 — ALL PASS.
- `--import` exit 0, 12 `.tres` 파싱 정상.

## 판정

**완료** — Step 6 완료 조건(12종 로드/검증, 4 카테고리 실행, combo 미입력)을 충족하고 P0/P1이 없다.
기존 3종 하드코딩 TurnAction은 데이터 스킬로 대체 가능하나 실제 BT 재배선+삭제는 밸런스 스왑·CB-001 회귀
때문에 별도 cleanup으로 이월(Task 참조). **SK-001 Step 0~6 전체 완료.**

## Step 6 Fix Follow-up

외부 리뷰의 **[P2] 테스트 강화** 지적을 반영했다(제품 코드 무변경).

- 문제: `GenerateAndValidate`가 파일이 없으면 `res://Assets/SkillData/Phase1`에 직접 저장해 커밋 데이터
  누락을 가릴 수 있었고, 로드 검증이 `IsValidV0`+`Effects.Count>0`뿐이라 값 드리프트(예: bow_shot HitChance,
  fist_jab MaxDamage)를 놓쳤다. 상세 확인은 sword_smash round-trip 하나뿐이었다.
- 수정:
  - 생성을 회귀 스위트에서 분리해 별도 부트스트랩 `Sk001Phase1DataGenerator`(+`.tscn`)로 옮겼다. 데이터
    팩 신규 생성/스펙 갱신 시에만 수동 실행한다.
  - 스펙을 공유 정본 `Phase1SkillSpec`(Build + Signature)로 추출했다.
  - 테스트는 `res://`에 쓰지 않고, 12개 파일 존재 강제 단언(`A.Exists[*]`) + `Signature`(Id/DisplayName/
    Range/Act/Windup/Mana/anim/side/selector + 이펙트 타입·파라미터 전부) 비교(`A.Match[*]`) + 디렉터리
    정확히 12개(`A.ExactlyTwelveFiles`)를 확인한다. round-trip도 서명 비교로 강화.
- 음성 검증: 커밋 bow_shot HitChance 90→85 변조 시 `A.Match[bow_shot]` FAIL(got 85 vs expected 90),
  복원 시 ALL PASS. 파일 삭제는 `A.Exists`가 잡는다.
- 재검증: `sk001_step6_data_pack_test` ALL PASS, 커밋 `.tres` 무변경, 회귀 유지.

## Related

- [[SK-001-Data-Driven-Skill-System]]
- [[ADR-018-Data-Driven-Skill-System]]
- [[Turn-System]]
- [[Article-Status-System]]
- [[BehaviorTree-System]]
- [[STEP_REVIEW_WORKFLOW]]