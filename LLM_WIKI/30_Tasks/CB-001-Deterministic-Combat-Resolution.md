---
id: CB-001
type: task
status: completed
system: Combat
created: 2026-07-05
updated: 2026-07-05
tags: [task, combat, turn-system, determinism, rng]
---

# CB-001 Deterministic Combat Resolution (Step 1 범위)

## Goal

같은 초기 상태(유닛 배치·스탯)와 같은 시드로 전투를 실행하면 항상 같은 결과(턴 순서, 타겟 선택,
데미지, 사망 순서)가 나오게 한다. 배속(`TurnHelper.Speed`)과 프레임레이트는 전투 결과에 영향을
주지 않아야 한다.

이 작업은 "1단계: 현 구조 유지, 비결정성 제거"만 다룬다. 시뮬레이션/프레젠테이션 계층 분리(2단계,
리플레이·헤드리스 빨리감기)는 후속 작업으로 미룬다([[Open-Tasks]] Later 참고).

## Context

### 현재 비결정성 원인 (2026-07-05 코드 조사 사실)

1. **시드 없는 전역 난수** — `Assets/Script/Article/Status/Affect/PhysicalDamage.cs:23`(크리티컬
   판정), `:31`(데미지 롤)이 `GD.RandRange`를 사용한다. Godot 전역 RNG는 실행마다 자동 시드되므로
   같은 전투가 실행마다 달라진다.
2. **순회 순서가 보장되지 않는 컬렉션으로 의사결정** —
   - `SkillUtil.GetAttackRangePositions`(`Assets/Script/TurnAction/Skill/SkillUtil.cs:9`)가
     `HashSet<Vector2I>`을 반환하고, `TurnActionBase.GetTarget`(`TurnActionBase.cs:59`)이 이 집합의
     열거 순서대로 `FirstOrDefault`로 타겟을 고른다. 사거리 안에 적이 여럿이면 누구를 때릴지가
     해시 순서에 좌우된다.
   - `TurnAction_ChainLightning.GetChainTarget`(`TurnAction_ChainLightning.cs:64`)도 동일 문제.
     `OrderBy(_hitTargets.Contains)` 뒤의 동점자 규칙이 없다.
   - `TurnHelper._Ready`(`TurnHelper.cs:48`)의 `List.Sort`는 불안정 정렬이라 `Priority`가 같은
     유닛끼리의 턴 순서에 명시적 규칙이 없다.
3. **프레임 수 종속 로직(실질 버그)** — `CharacterArticle.TurnPlay`(`CharacterArticle.cs:43`)가
   `ApplyAffectingStatuses()`를 "턴마다"라는 주석과 달리 자기 턴 동안 매 물리 프레임 호출한다.
   턴이 걸리는 프레임 수는 애니메이션 길이 × `Speed`에 따라 달라지므로 지속 효과 적용 횟수가
   배속·프레임레이트에 종속된다.
4. **이동 목적지 동점자 규칙 부재** — `BehaviorTree_Move.FindTarget`(`BehaviorTree_Move.cs:54-59`)의
   `OrderBy(경로 길이).ThenBy(LengthSquared)` 이후 동점은 후보 나열 순서에 맡겨져 있다.
   같은 패턴이 `BehaviorTree_MultipleMove.FindPath`에도 있으므로 Step 3에서 함께 고정한다.

다행히 전투의 권위 상태는 `TilePosition`(정수)과 int 스탯이라 부동소수점 플랫폼 오차 문제는 없다.
Tween/애니메이션의 float 값은 연출에만 쓰인다.

### 관련 문서

- [[Turn-System]], [[Article-Status-System]], [[BehaviorTree-System]]
- 정적 가드 테스트 선례: `addons/world_core/save_game/tests/sg001_step1_static_guard_test.gd`
- C# 헤드리스 테스트 선례: `addons/behaviortree/tests/BehaviorTreeValidationTest.cs`(BT-001)

## Scope

- 지속 효과 적용을 턴 시작 시 정확히 1회로 고정.
- 전투 전용 시드 RNG(`CombatRng`) 도입과 전투 코드의 `GD.Rand*` 제거.
- 타겟 선택·턴 순서·이동 목적지의 명시적 정규 순서(동점자 규칙) 부여.
- 결정론 회귀 테스트(같은 시드 2회 실행 → 동일 결과)와 정적 가드 테스트.

## Out of Scope (Non-Goals)

- 시뮬레이션/프레젠테이션 계층 분리(TurnResolver + 이벤트 로그). 2단계 후속.
- `Damage.ApplyImmediately`의 `DamageFloater`/`Hit()` UI 직접 호출 제거(`Damage.cs:51`). 헤드리스
  판정 테스트의 걸림돌이지만 이 작업에서는 현 결합을 유지한 채 씬 포함 테스트로 검증한다. 2단계 후속.
- `AStarGrid2D` 대체 자체 경로탐색. 엔진 버전 고정을 전제로 현 API를 유지한다(엔진 버전 간
  리플레이 호환은 2단계 이후 관심사).
- AoE(`Scale`) 타겟팅 구현, 새 스킬 추가, 데미지 공식 밸런스 변경.
- FX/사운드의 난수 사용 제한(연출 전용 난수는 자유. 단 전투 RNG를 소비하지 않는다는 규칙만 성립).

## Design

### 규칙 (ADR-017로 확정 예정)

1. 전투 결과에 영향을 주는 모든 난수는 전투 시작 시 시드된 단일 `RandomNumberGenerator`
   (`CombatRng`)에서만 소비한다. 소비는 판정 시점(턴 시작, 액션 판정)에서만 일어나고, 프레임
   콜백(`RunPhase`류)에서는 소비하지 않는다. 연출은 별도 난수를 쓴다.
2. 후보가 여럿인 모든 의사결정(타겟, 체인 대상, 이동 목적지, 턴 순서)은 명시적 정규 순서를
   따른다. 기본 정규 순서: 거리 오름차순 → 타일 Y → 타일 X. 턴 순서는 `Priority` → 스폰 순번.
3. 상태 변경(지속 효과 적용 포함)은 턴 해석의 이산 지점에서만 일어난다. 프레임 수·배속은 결과에
   영향을 줄 수 없다.

### 구현 방향

- `CombatRng`는 `TurnHelper`가 소유한다. 시드는 전투 시작 시 결정하고 기록하며, 재현 테스트를 위해
  `[Export]` 고정도 가능하게 한다. 접근은 기존 관례대로 `BattleFieldScene.BattleField.TurnHelper`
  경유로 한다.
- `SkillUtil.GetAttackRangePositions`는 정규 순서로 정렬된 `IReadOnlyList<Vector2I>`를 반환하도록
  변경한다. 파급: `TurnActionBase.AttackRangePositions`(`TurnActionBase.cs:32`),
  `CharacterArticle.AttackRangePositions`(`CharacterArticle.cs:22`), ChainLightning.
- 스폰 순번은 `ArticlesContainer._Ready`(`Assets/Script/ArticlesContainer.cs:19`)의 씬 트리 순회 순서(결정적)를
  근거로 유닛에 부여한다.
- `ApplyAffectingStatuses()` 호출은 `TurnHelper`가 턴을 넘기는 지점(`GetNextTurnArticle` 직후,
  새 살아있는 유닛 턴 시작)에서 1회로 이동한다.

## Steps

### Step 0 — Design Review + ADR

Scope:

- 이 문서의 원인 분석이 현재 코드와 맞는지 대조한다.
- 위 규칙 1~3을 `ADR-017-Deterministic-Combat-Resolution`으로 작성한다(정규 순서·RNG 소비 규약·
  이산 판정 지점 정의).
- 제품 코드는 수정하지 않는다.

Done condition: 설계 리뷰 판정 Approved(또는 Approved after design fixes), ADR-017 작성.

Result:

- 완료(2026-07-05): 실제 코드와 대조 후 [[ADR-017-Deterministic-Combat-Resolution]] 작성.
- 판정: **Approved after design fixes**.
- 설계 보강 반영:
  - `CombatRng` 소유자를 `TurnHelper`로 확정.
  - `BehaviorTree_MultipleMove.FindPath`도 Step 3 이동 동점자 고정 범위에 포함.
  - `ApplyAffectingStatuses()`는 새 살아있는 유닛 턴 시작 직후 1회 적용으로 명확화.
  - 정적 가드는 전투 결과 코드의 `GD.Rand*`/`new Random(`/`Guid.NewGuid` 직접 사용을 막되, FX 등 연출
    전용 난수는 전투 RNG를 소비하지 않는 별도 계층으로 허용.

### Step 1 — 턴 시작 시 지속 효과 1회 적용

Scope:

- `CharacterArticle.TurnPlay`의 매 프레임 `ApplyAffectingStatuses()` 호출을 턴 시작 1회로 이동.
- 배속 불변 검증: 같은 전투를 `Speed` 값을 달리해 실행해도 지속 효과 적용 횟수가 동일함을
  테스트로 고정한다.

Done condition: 지속 효과가 유닛 턴당 정확히 1회 적용됨을 검증하는 헤드리스 테스트 PASS.
기존 동작 대비 변화(효과가 약해짐)는 의도된 수정임을 이 문서에 기록한다.

Result:

- 완료(2026-07-05): `CharacterArticle.TurnPlay()`의 매 frame `ApplyAffectingStatuses()` 호출을 제거하고,
  `ITurnAffectedArticle.ApplyTurnStartEffects()` hook을 통해 `TurnHelper.AdvanceToNextTurn()` 직후 1회만
  지속 효과를 적용하도록 이동했다.
- Game over 상태(`_turnAffectedArticleList.Count <= 1`, 한쪽 팀 0명 등)에서는 새 턴 효과를 적용하지 않는다.
- 의도된 동작 변화: 기존에는 Running 액션/애니메이션이 길거나 `Speed`가 낮을수록 지속 효과가 여러 번
  적용됐지만, 이제 유닛 턴 시작당 1회만 적용되어 효과가 약해질 수 있다. 이는 프레임 수 종속 버그 수정이다.
- 신규 검증: `Assets/Script/Tests/cb001_step1_turn_effect_test.tscn` / `Cb001Step1TurnEffectTest.cs`.
  새 턴 1회 적용, `Speed=1`/`Speed=4`에서 Running frame 반복 재적용 없음, 턴 완료 후 다음 유닛 1회 적용,
  game over 상태 미적용을 단언한다.

### Step 2 — 전투 시드 RNG 도입 + 정적 가드

Scope:

- `CombatRng` 도입, `PhysicalDamage`의 두 `GD.RandRange`를 `CombatRng`로 대체.
- 정적 가드 테스트 추가: `Assets/Script` 아래에서 `GD.Rand`/`new Random(`/`Guid.NewGuid` 사용을
  금지 검사한다(sg001 정적 가드 패턴을 C# 소스 대상으로 적용).

Done condition: 같은 시드로 같은 (giver, recipient) 판정을 반복 실행하면 동일한 크리/데미지
시퀀스가 나온다. 정적 가드 PASS.

Result:

- 완료(2026-07-05): `TurnHelper`가 전투 전용 `RandomNumberGenerator`를 소유한다. `_combatSeed` export,
  `CombatSeed`, `ResetCombatRng(seed)`, `CombatRandRange(double,double)`, `CombatRandRange(int,int)` API를 추가했다.
  `_Ready()`에서 seed를 초기화한다.
- `PhysicalDamage`의 크리티컬 판정과 데미지 롤이 `GD.RandRange` 대신
  `BattleFieldScene.BattleField.TurnHelper`의 `CombatRng`를 소비한다. BattleField/TurnHelper가 없는 경로는
  전투 context 위반으로 `InvalidOperationException`을 던진다.
- 신규 검증: `Assets/Script/Tests/cb001_step2_combat_rng_test.tscn` / `Cb001Step2CombatRngTest.cs`.
  같은 seed의 `TurnHelper` RNG 시퀀스 재현, 같은 seed의 `PhysicalDamage` 크리/데미지 시퀀스 재현,
  `Assets/Script` 제품 C# 코드의 `GD.Rand`/`new Random(`/`new System.Random(`/`Random.Shared`/`Guid.NewGuid` 직접 사용 금지 정적 가드를 단언한다.
- 정적 검색 보조 확인: `rg -n "GD\.Rand|new Random\(|new System\.Random\(|Random\.Shared|Guid\.NewGuid" Assets/Script --glob "*.cs" --glob "!Tests/**"`
  결과 매치 없음(exit 1).

### Step 3 — 의사결정 순서 결정화

Scope:

- `SkillUtil.GetAttackRangePositions` 정규 순서 반환으로 변경(파급 지점 포함).
- `TurnActionBase.GetTarget`, `TurnAction_ChainLightning.GetChainTarget`에 규칙 기반 정렬(거리 →
  Y → X) 적용.
- `TurnHelper` 턴 순서를 `Priority` → 스폰 순번의 안정 키 정렬로 교체(스폰 순번 부여 포함).
- `BehaviorTree_Move.FindTarget`와 `BehaviorTree_MultipleMove.FindPath`에 최종 동점자 규칙(목적지 Y → X) 추가.

Done condition: 동점 상황(사거리 내 적 2 이상, 같은 Priority 유닛 2 이상, 등거리 이동 후보)을
구성한 헤드리스 테스트에서 선택 결과가 규칙과 일치하고 반복 실행 간 동일하다.

Result:

- 완료(2026-07-05): 정규 순서 공용 구현을 `SkillUtil`로 모았다 — `ManhattanDistance`,
  `OrderByCanonical`/`ThenByCanonical`(거리 → Y → X), `SelectCanonicalPath`(경로 길이 → 목적지 거리² →
  목적지 Y → 목적지 X).
- `SkillUtil.GetAttackRangePositions`가 정규 순서로 정렬된 `IReadOnlyList<Vector2I>`를 반환한다. 파급 반영:
  `TurnActionBase.AttackRangePositions`, `CharacterArticle.AttackRangePositions`(둘 다 `HashSet` →
  `IReadOnlyList` 타입 변경).
- `TurnActionBase.GetTarget`: 사거리 내 생존 적을 정규 순서로 정렬해 선택(기존 `FirstOrDefault` 해시 순서
  의존 제거). `TurnAction_ChainLightning.GetChainTarget`: 미타격 우선 유지 + `ThenByCanonical` 동점자 추가.
- 턴 순서: `ArticleBase.SpawnIndex` 추가, `ArticlesContainer._Ready()`가 씬 트리 순회 순서로 부여,
  `ITurnAffectedArticle`에 `SpawnIndex` 노출. `TurnHelper`는 `List.Sort`(불안정) 대신
  `SortTurnAffectedArticles()`에서 `Priority` → `SpawnIndex` 키 정렬을 사용한다.
- `BehaviorTree_Move.FindTarget`/`BehaviorTree_MultipleMove.FindPath`가 `SkillUtil.SelectCanonicalPath`를
  공유한다(기존 각자 구현 + 최종 동점자 부재 제거).
- 예상된 관찰 변화(Failure Policy에 따른 기록): 사거리 내 적이 여럿일 때 첫 타겟이 기존 해시 열거 순서
  대신 정규 순서 대상으로 바뀔 수 있고, 같은 Priority 유닛의 턴 순서가 스폰 순번 순서로 고정된다.
  기존 순서는 빌드/런타임 내부 상태에 의존했으므로 별도 기준선 비교는 수행하지 않았다. Step 4 결정론
  회귀가 새 규칙 기준의 기준선을 고정한다.
- 신규 검증: `Assets/Script/Tests/cb001_step3_canonical_order_test.tscn` / `Cb001Step3CanonicalOrderTest.cs`
  12/12 ALL PASS — 사거리 오프셋 정규 순서(거리 1·2 전체 시퀀스 + 반복 동일성), 타겟 동점자(거리 → Y → X),
  체인식 선행 키 + 정규 동점자, 턴 순서 `Priority` → `SpawnIndex` 안정 정렬, 경로 후보 동점자(길이 우선,
  Y/X 타이브레이크, 짧은 후보 필터, 무후보 null).
- 회귀: Step 1·Step 2·BT-001 헤드리스 ALL PASS, `dotnet build` 경고/오류 0, `--import` 0 parse error.
- 기존 `Cb001Step1TurnEffectTest`의 `FakeTurnArticle`에 인터페이스 확장(`SpawnIndex`)을 반영했다.
- 리뷰 지적(P2, 비블로킹): Step 3 테스트는 정규 순서 프리미티브를 직접 검증하지만,
  `GetTarget`/`GetChainTarget`/`FindTarget`/`FindPath`가 실제로 그 프리미티브를 호출하는지의 배선 레벨
  보증은 `BattleFieldScene` 싱글턴 의존 때문에 씬 통합이 필요하다. 이 보증은 Step 4 결정론 회귀(실제 전투
  씬 실행)가 담당한다 — Step 4 Scope에 명시함.

### Step 4 — 결정론 회귀 테스트

Scope:

- 대표 전투 씬(근접 + 체인라이트닝 포함 조합)을 같은 시드로 2회 헤드리스 실행해 결과 로그(턴
  순서, 타겟, 데미지 수치, 사망 순서, 최종 HP/위치)가 완전히 일치함을 단언한다.
- 다른 시드에서는 결과가 달라질 수 있음(스모크)과, 같은 시드 + 다른 `Speed` 배속에서 결과가
  동일함을 함께 단언한다.
- 대표 전투 씬에는 단일 이동/다중 이동/체인라이트닝 유닛을 포함해, Step 3 정규 순서 프리미티브가
  `GetTarget`/`GetChainTarget`/`FindTarget`/`FindPath` 실제 배선에서 소비됨을 씬 레벨에서 보증한다
  (Step 3 리뷰 P2 종결 조건).

Done condition: 결정론 회귀 ALL PASS, SCRIPT ERROR 0, `--import` 0 parse error.

Result:

- 완료(2026-07-05): `Assets/Script/Tests/cb001_step4_determinism_test.tscn` / `Cb001Step4DeterminismTest.cs`.
  실제 `battle_field.tscn`을 인스턴스화해 4회 실행하고 이벤트 로그(턴 전환 순서, HP 변화(데미지 수치),
  사망 순서, 최종 HP/타일 위치)를 문자열로 비교한다. 8/8 ALL PASS, SCRIPT ERROR 0.
  - [A] 같은 시드(777) + 같은 배속(2) 2회 → 로그 완전 일치.
  - [B] 다른 시드(999) → 로그 상이(스모크). 아군 피격(HP 변화) 존재를 전제 조건으로 단언해
    RNG가 실제 소비됐음을 보장한다.
  - [C] 같은 시드(777) + 다른 배속(6) → 로그 완전 일치(배속 불변).
  - [D] 근접 공격/체인 데미지/사망 기록 존재 — Step 3 리뷰 P2 종결: `GetTarget`(근접),
    `GetChainTarget`(체인), `FindTarget`(단일 이동), `FindPath`(다중 이동, Ally 기본 BT)가 실제 씬
    배선에서 소비됨을 보증.
- 씬 구성 사실: 출하된 `battle_field.tscn`의 Opponent Puppet 4기는 **BehaviorTree가 비어 있어** 근접
  공격·단일 이동·`PhysicalDamage`(전투 RNG) 경로가 실행되지 않는다. 테스트가 Puppet 1기에 근접
  BT(Selector → FindOpponent → Move, TurnAction_Attack)를 프로그램적으로 구성하고 HP를 크게 올려
  전투 내내 공격을 지속시킨다(RNG 롤 수십 회 확보).
- 구현 중 발견/수정한 테스트 설계 이슈 2건(제품 코드 아님):
  1. 초기 구성에서 근접 공격이 1회뿐이라 데미지 롤(30~35 택1)이 다른 시드와 우연히 일치해 [B]가
     실패했다 → 근접 Puppet HP 버프로 롤 표본을 ~30회로 확대.
  2. 근접 Puppet을 아군 옆에 재배치했더니 아군이 이동/체인 확산 없이 조기 사망해 사망 기록이 없었다
     → 원위치(군집 근처) 유지로 해결.
- 실행 시간: `Engine.TimeScale = 16`으로 4회 전투 총 수 초. 전투당 물리 프레임 ~500.
- 회귀: Step 1·2·3, BT-001 ALL PASS. `--import` 0 parse error, `dotnet build` 경고/오류 0.

### Step 5 — 문서와 완료 리뷰

Scope:

- [[Turn-System]], [[Article-Status-System]]에 결정론 규약(시드 RNG, 정규 순서, 턴당 1회 효과)의
  현재 사실을 반영한다.
- [[Current-State]] 요약, [[Open-Tasks]] 이동, `LLM_WIKI/50_Reviews/CB-001-...-Review.md` 작성.

Result:

- 완료(2026-07-05): [[Turn-System]](Flow/Combat RNG/Canonical Order/Known Gaps/Verification)과
  [[Article-Status-System]](턴 시작 1회 적용, Damage RNG)은 Step 1~4 진행 중 현재 사실로 갱신 완료.
- 완료 회귀 matrix 재실행: `--import` 0 parse error, CB-001 Step 1~4 + BT-001 테스트 5종 **ALL PASS**,
  SCRIPT ERROR 0, `dotnet build` 경고/오류 0.
- 리뷰 문서 [[CB-001-Deterministic-Combat-Resolution-Review]] 작성 — **판정: 완료**.
- [[Current-State]] Combat 섹션을 완료 요약으로 정리, [[Open-Tasks]]에서 Next → Recently Completed 이동.

## Completion Criteria

- 같은 시드 + 같은 초기 배치 → 같은 전투 결과(결정론 회귀 테스트로 고정).
- `Speed` 배속·프레임 수가 전투 결과에 영향을 주지 않는다.
- 전투 코드(`Assets/Script`)에 `GD.Rand*` 직접 사용이 없음을 정적 가드가 보장한다.
- 타겟/턴 순서/이동의 동점자 규칙이 ADR-017에 문서화되고 테스트로 검증된다.
- SCRIPT ERROR 0, `--import` 0 parse error, 기존 BT-001 헤드리스 회귀 GREEN.

## Failure / Mismatch Policy

- Step 1에서 지속 효과의 프레임당 중복 적용에 의존하는 기존 밸런스/테스트가 발견되면 임의로
  보정하지 말고 Design Deviation으로 보고한다.
- Step 3의 정렬 변경으로 기존 전투 씬의 관찰 결과(누굴 먼저 때리는가)가 달라지는 것은 예상된
  변화다. 달라진 사례를 이 문서에 기록한다.
- `HashSet` → 정렬 리스트 변경이 예상 밖의 컴파일 파급을 만들면 범위를 좁히지 말고 파급 지점을
  전부 나열해 보고 후 진행한다.

## Step 0 Design Review

검토 대상:

- Task: 이 문서
- ADR: [[ADR-017-Deterministic-Combat-Resolution]]
- 관련 시스템: [[Turn-System]], [[Article-Status-System]], [[BehaviorTree-System]]
- 실제 코드: `TurnHelper`, `CharacterArticle`, `PhysicalDamage`, `TurnActionBase`, `TurnAction_ChainLightning`,
  `SkillUtil`, `BehaviorTree_Move`, `BehaviorTree_MultipleMove`, `ArticlesContainer`

### Findings

- **[P2] 이동 결정화 범위가 `BehaviorTree_Move`에만 적혀 있어 `BehaviorTree_MultipleMove`가 남을 수 있음**
  - 실제 `Assets/Script/AutoCrawlerBehaviorTree/Action/BehaviorTree_MultipleMove.cs`도 같은
    `OrderBy(path.Count).ThenBy(distance).FirstOrDefault()` 패턴을 사용한다.
  - 대표 전투가 `MultipleMove`를 쓰면 같은 seed 회귀에서도 이동 목적지 동점자가 후보 나열 순서에 의존할 수 있다.
  - 조치: Step 3 범위와 ADR-017 적용 범위에 `BehaviorTree_MultipleMove.FindPath`를 포함했다.

- **[P2] RNG 소유권이 `TurnHelper` 또는 `BattleFieldScene`으로 열려 있어 구현 API가 흔들릴 수 있음**
  - `PhysicalDamage`는 `ArticleStatus` 경로에서 생성되므로 전투 context 접근 규칙을 미리 고정해야 한다.
  - 조치: ADR-017에서 `TurnHelper` 소유, `BattleFieldScene.BattleField.TurnHelper` 접근으로 확정했다.

- **[P3] 문서 경로 일부가 실제 코드 위치와 다름**
  - `ArticlesContainer`는 `Assets/Script/ArticlesContainer.cs`에 있다.
  - 조치: 구현 방향의 파일 경로를 실제 위치로 정정했다.

### Open Decisions

- 없음. Step 1 구현에 필요한 설계 결정은 ADR-017로 확정했다.

### Step Assessment

- Step 1은 독립적으로 진행 가능하다. 단, 죽은 유닛/게임오버/초기 첫 턴에서도 지속 효과가 정확히 1회만
  적용되는지 테스트해야 한다.
- Step 2는 Step 1과 독립적이지만, `CombatRng` public API를 작게 유지해야 한다.
- Step 3은 `HashSet` 반환 타입 변경 파급이 있으므로 컴파일 오류를 통해 모든 소비 지점을 확인해야 한다.
- Step 4는 `Damage.ApplyImmediately` UI 결합 때문에 순수 단위 테스트보다 씬 포함 테스트가 현실적이다.

### Verification Assessment

- Step 1 테스트에는 서로 다른 `Speed`와 복수 physics frame 경과를 포함해야 한다.
- Step 2 정적 가드는 `Assets/Script` C# 파일을 대상으로 하되, 테스트 코드와 연출-only 경로의 예외 정책을
  명확히 해야 한다.
- Step 3 테스트는 기본 공격, 체인라이트닝, 단일 이동, 다중 이동을 모두 포함해야 한다.
- Step 4 결과 로그에는 턴 순서, 타겟, 피해량, 사망 순서, 최종 HP/위치를 포함해야 한다.

### Verdict

**Approved after design fixes**. 위 보강 사항을 이 문서와 [[ADR-017-Deterministic-Combat-Resolution]]에
반영했으므로 다음 세션/요청에서 Step 1 구현으로 진행 가능하다.
## Decisions

- [[ADR-017-Deterministic-Combat-Resolution]] accepted.

## Follow-ups

- 프로덕션 전투 seed 생성/기록 정책: 현재 `_combatSeed` 기본값은 재현 테스트용 고정값 1이다. 실제 새 전투마다 seed를 생성하고 로그/리플레이 입력에 기록하는 경로는 Step 4 시드 주입 회귀 또는 Step 5 문서/완료 리뷰에서 결정한다.
- `CharacterArticle.AttackRangePositions`의 액션 후보 선택(`OrderByDescending(count)`)에 count 동점자
  규칙이 없다. 트리 순회 순서가 결정적이라 실제 비결정성은 없지만, ADR-017의 "명시적 동점자" 원칙에서
  유일하게 벗어난 후보 선택이다(Step 3 리뷰 P3).
- 정적 가드 패턴 보강: `new System.Random(`, `Random.Shared`는 현재 금지 패턴에 걸리지 않는다(Step 2 리뷰 P3).
- 2단계: 시뮬레이션/프레젠테이션 분리 — 순수 `TurnResolver` + 이벤트 로그(`Moved`/`Attacked`/
  `Damaged`/`Died`), 연출 계층은 이벤트 재생만. 리플레이 포맷 `{시드, 초기 배치, 스탯}`.
- `Damage.ApplyImmediately`의 `DamageFloater`/`Hit()` UI 결합 제거(이벤트 발행으로 전환).
- 엔진 버전 간 리플레이 호환이 필요해지는 시점에 자체 결정론 경로탐색 검토.
- AoE(`Scale`) 구현 시 명중 적용 순서에 정규 순서 적용.

## Related

- [[Turn-System]]
- [[Article-Status-System]]
- [[BehaviorTree-System]]
- [[STEP_REVIEW_WORKFLOW]]
