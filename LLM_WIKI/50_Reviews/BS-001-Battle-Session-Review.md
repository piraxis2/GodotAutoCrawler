---
type: review
task: BS-001-Battle-Session
step: 0
status: complete
reviewed: 2026-07-13
tags: [review, combat, battle-session, lifecycle, design-review]
---

# Design Review: BS-001 BattleSession Runtime Boundary (Step 0)

이 문서는 BS-001 Step 0(Design Review + ADR)의 결과다. 제품 코드/`.tscn`/`.tres`/`project.godot`는
수정하지 않았고, 리뷰 결론 반영으로 Task/ADR 문서만 갱신했다(아래 findings의 `-> 반영` 표기).

## 검토 대상

- Task: [[BS-001-Battle-Session]]
- ADR: [[ADR-022-Battle-Session-Lifecycle]]
- 대조 시스템: [[Turn-System]], [[Article-Status-System]], [[Skill-System]]
- 기획 근거: `GameDesign/기획서/08_데모_스코프.md` U1, `11_던전데이터_설계.md` — repo 밖 외부 기획(대조는 Task
  전사문 기준).
- 대조 코드(실측):
  - `Assets/Script/TurnHelper.cs`(`_Ready` 일괄 부팅, `_PhysicsProcess` 턴+FX, `IsGameOver`, cursor)
  - `Assets/Script/BattleFieldScene.cs`(정적 `_battleFieldScene`, `_ExitTree` 없음)
  - `Assets/Script/ArticlesContainer.cs`(`Articles` dict, `SpawnIndex` 부여, `OnDead` 제거 구독)
  - `Assets/Script/Article/ArticleBase.cs`(`sealed override _Ready`, `Dead()`, `OnDead`, `IsAlive`)
  - `Assets/Script/Article/CharacterArticle.cs`(`ChargeBattleAmmo`, `ApplyTurnStartEffects`, `TurnPlay`)
  - `Assets/Script/Article/Status/Element/Health.cs`(`CurrentHealth` setter)
  - `Assets/Script/Article/Status/ArticleStatus.cs`(`HasLivingHealth`)
  - `Assets/Scenes/Map/battle_field.tscn`(참가자·TurnHelper 직접 포함, `resource_local_to_scene`)
  - `project.godot`(`run/main_scene`)

## 코드 대조 결과 (Task 현황 서술 검증)

Task의 "Current code facts(2026-07-13)" 서술은 실측과 일치한다. 추가로 확정한 사실:

- `TurnHelper._Ready()`가 seed reset → 참가자 수집 → OnDead 구독 → `ChargeBattleAmmo()` → 정렬 →
  `AdvanceToNextTurn()`를 한 번에 수행한다(`TurnHelper.cs:29~58`). `_PhysicsProcess`가 `FxPlayer.Tick`과 턴
  실행을 함께 진행한다(`TurnHelper.cs:71~95`). Step 1의 seam/이관 대상이 코드로 확인된다.
- `IsGameOver`는 참가자 수 ≤ 1, 현재 턴 null, `Opponent`/`Ally` 공백으로 판정하고 종료 시 TODO에서 조용히
  멈춘다(`TurnHelper.cs:23,77~81`). PC 개념이 없다 — ADR §3(PC 판정을 TurnHelper 밖으로)의 근거가 맞다.
- cursor 결함 실재: 현재 턴 유닛 사망 시 OnDead 핸들러가 리스트에서 먼저 제거되고
  (`TurnHelper.cs:44~47`), `GetNextTurnArticle`이 `IndexOf(current) == -1`(→ -1) 뒤 `(-1+1)%count == 0`으로
  리스트 첫 유닛으로 리셋된다(`TurnHelper.cs:120~128`). Step 1 수정 대상 정확.
- `BattleFieldScene`는 `_Ready`에서 정적 `_battleFieldScene = this`만 하고 `_ExitTree` 정리가 없다
  (`BattleFieldScene.cs:26~29`). Step 3에서 추가할 안전 정리 대상 정확.
- `ArticlesContainer._Ready`가 `SpawnIndex`를 부여하고 진영별 dict에 담으며 각 article의 `OnDead`에 dict 제거를
  구독한다(`ArticlesContainer.cs:17~26`). `AddChild(battleScene)` 시 동기 `_Ready`로 이 dict가 채워지므로,
  세션이 "AddChild 직후 참가자/PC 해석"하는 설계 전제가 성립한다. `SessionUnitId = Faction + SpawnIndex`의
  `SpawnIndex` 소스도 여기다.
- **`run/main_scene`은 `workspace.tscn`(`uid://caplqnfi4j3ys`)이며 `battle_field.tscn`이 아니다**
  (`project.godot:18`, WS-002에서 전환됨). 즉 부팅 시 정적 `BattleFieldScene.BattleField`는 null이라 U1
  test/host 경로에서 단일 세션 가드가 깨끗하게 시작한다(Finding 5).
- **`battle_field.tscn`의 `ArticleStatus`/`StatusElement` sub-resource는 `resource_local_to_scene = true`**
  (`battle_field.tscn:28,64,69` 등)라 `PackedScene.Instantiate()`마다 상태가 복제된다 — 세션 2회 연속 실행 시
  HP/상태 오염이 없다는 전제가 코드로 확인된다(SkillAmmoState/StatusController는 애초에 씬에 직렬화되지 않는 순수
  C#).

## Findings

1. **[P1] 사망 구동 완료 teardown이 `TurnHelper._PhysicsProcess` 콜스택 안에서 실행되면 tree 수정/재진입이 위험하다.**
   - 근거: 사망 발생 경로는 `TurnHelper._PhysicsProcess`(`cs:90`) → `_currentTurnArticle.TurnPlay` → 피해 적용
     → `Health.CurrentHealth` setter(`Health.cs:22`) → `Owner.Dead()` → `EmitSignal("OnDead")`
     (`ArticleBase.cs:99`)다. 세션의 `OnDead` 핸들러는 **현재 `_PhysicsProcess` 중인 전투 씬의 자손 노드**에서
     동기적으로 호출된다.
   - 문제: Task/ADR의 Completion 순서(StopBattle → capture → unsubscribe → `RemoveChild` → `_ExitTree` →
     `QueueFree` → Completed)를 이 콜스택 안에서 동기 실행하면, 자기 자신의 `_PhysicsProcess`가 아직 언와인딩
     중인 서브트리를 `RemoveChild`/`QueueFree`하게 된다(Godot 전형적 tree-modify-during-iteration 위험). 나아가
     Completed 핸들러가 즉시 session2를 시작하면 이전 물리 프레임이 끝나기 전에 정적 context를 다시 만진다.
   - 영향: Step 2/3 착수 전에 완료 계약을 고정해야 한다. 완료 타이밍이 "사망 프레임 동기"가 아니라 "다음 idle
     frame deferred"로 바뀌므로 테스트 단언 형태가 달라진다.
   - 권장: **OD4의 deferred 완료를 invalid-setup뿐 아니라 모든 완료 경로(사망 구동 Victory/Defeat 포함)로 확대**
     한다. 완료 감지 즉시 완료 latch만 동기 설정하고(Finding 4), teardown+event 블록 전체를 `CallDeferred`로
     한 단위로 idle frame에 실행한다. 이러면 "RemoveChild가 Completed보다 먼저"와 same-frame 재시작 가드가
     자연히 성립한다. -> Task Failure Policy/Lifecycle/OD4에 반영.

2. **[P1] `OnDead` 시점에 사망 유닛의 HP/생존이 아직 사망 전 값이라 결과 캡처가 stale를 기록할 수 있다.**
   - 근거: `Health.CurrentHealth` setter는 `if (value <= 0) Owner.Dead();`(→ OnDead emit)를 **먼저** 호출하고
     그 다음에 `_currentHealth = Math.Clamp(...)`로 0을 대입한다(`Health.cs:22~24`). `HasLivingHealth()`는
     `health.CurrentHealth > 0`을 본다(`ArticleStatus.cs:80`). 따라서 `OnDead` 콜백이 도는 순간 사망 유닛은 아직
     `IsAlive == true`이고 `CurrentHealth ==` 직전 양수값이다.
   - 문제: 세션이 `OnDead`에서 `BattleUnitResult.FinalHealth`/`Survived`를 라이브로 읽으면, 방금 죽은 유닛을
     `Survived = true` + `FinalHealth > 0`으로 잘못 기록한다. 승패 판정을 사망 유닛의 `IsAlive` 재조회로 하면
     오판한다.
   - 영향: Step 3 결과/판정 계약의 정확성 문제. P0는 아니나(데이터 파괴/실행 불가 아님) 핵심 완료 조건("참가자별
     생존/최종 HP 보존", "PC 사망=Defeat")을 직접 위협한다.
   - 권장: **`OnDead` 이벤트의 identity를 진실로 취급**한다 — 이벤트를 올린 article은 죽은 것이므로, 그 순간의
     라이브 `CurrentHealth`/`IsAlive` 재조회와 무관하게 해당 유닛을 `Survived = false`, `FinalHealth = 0`(또는
     null)로 기록한다. 완료 판정도 죽은 유닛을 재스캔하지 말고 이벤트 identity로 처리한다. -> Task Proposed
     Contracts(BattleUnitResult 주석)/Step 3에 반영.

3. **[P2] 진영 전멸(Victory) 판정이 `ArticlesContainer`의 `OnDead` 구독 순서에 의존한다.**
   - 근거: "상대 생존자 0"의 자연 신호는 `Articles["Opponent"].Count == 0`인데 이 dict는 ArticlesContainer 자신의
     `OnDead` 핸들러가 지운다(`ArticlesContainer.cs:23`). 세션은 Start에서 더 늦게 구독하므로 현재 순서에서는
     ArticlesContainer가 먼저 제거해 count가 맞다. 그러나 이는 구독 순서 의존이고 문서화돼 있지 않다.
   - 문제: 구독 순서가 바뀌면 사망 프레임에 Victory를 놓친다. TurnHelper는 U1 이후 game-over를 소유하지 않으므로
     "다음 프레임 IsGameOver"라는 안전망도 사라진다.
   - 권장: 세션이 Start에서 추적한 상대 집합에 대해 **자체 bookkeeping**으로 판정한다(추적 상대의 `OnDead`마다
     자기 count 감소, Finding 2의 event identity 사용). ArticlesContainer dict에 판정을 의존하지 않는다. dict를
     유지하려면 순서 의존을 Step 3에 명시. -> Task Lifecycle/Step 3에 반영.

4. **[P2] 같은 프레임 다중 사망에서 Completed 1회를 보장하는 latch의 설정 시점이 명시돼야 한다.**
   - 근거: ADR Risks가 이미 지적한다. 구체 위험: 한 물리 프레임에 여러 `OnDead`가 날 수 있고(예: 광역), PC 사망과
     상대 전멸이 같은 프레임에 동시 참일 수 있다.
   - 권장: 완료 latch를 **첫 완료 감지 시점에 동기적으로** 설정하고(teardown은 Finding 1대로 deferred여도), 같은
     프레임 두 번째 `OnDead`는 no-op이 되게 한다. deferred 블록 내부에서도 latch 재확인. -> Task Lifecycle에 반영.

5. **[P2] PC 사망과 상대 전멸이 같은 프레임에 동시 발생할 때 outcome 우선순위가 미정이다.**
   - 근거: Completion Criteria는 "PC 사망은 다른 아군 생존 여부와 무관하게 패배"를 규정하지만, **PC가 마지막 상대를
     죽이며 같은 프레임에 함께 죽는 상호 사망(mutual kill)** 시 Defeat/Victory 중 무엇인지 규정하지 않는다.
   - 문제: 결정론(CB-001) 시스템에서 이 우선순위가 미정이면 같은 시드가 프레임 처리 순서에 따라 다른 outcome을 낼 수
     있다. 완료 조건 단언 불가.
   - 권장: 결정적 우선순위를 못 박는다. ADR §3이 PC-specific defeat를 강조하므로 **Defeat 우선(PC 사망을 먼저
     평가)**을 권장한다. Open Decision으로 사용자 확정 대상. -> Task Open Decisions에 신규 항목으로 추가.

6. **[P3] `main_scene`이 `workspace.tscn`이라 U1은 안전하나, U3에서 workspace World 창 안에 전투 씬을 올리면 정적 context가 세션 밖에서 설정될 수 있다.**
   - 근거: `project.godot:18` `run/main_scene = uid://caplqnfi4j3ys`(workspace). 부팅 시 정적
     `BattleFieldScene.BattleField`는 null → 단일 세션 가드가 깨끗하게 시작(설계에 유리).
   - 문제: BS-001 범위 밖이지만, U3가 workspace 안에서 battle scene을 임베드/부팅하면 세션이 소유하지 않은 static
     설정이 생겨 단일 세션 가드와 충돌할 수 있다.
   - 권장: U1은 test/host가 static을 소유한다는 전제를 유지하고, workspace 임베드 통합은 후속(WS-002 후속 "전장
     scene 연결")에서 세션 소유권과 함께 다룬다고 명시. -> Task Follow-ups에 반영.

## Open Decisions 확정 (Step 0 완료 조건)

Task Open Decisions 4건 + Finding 5 신규 1건을 확정한다.

1. **Running 중 외부 tree removal 정책 -> 권장 채택(내부 정리만, C# event 미발행).** 소유자가 제거 중인 객체에서
   콜백 재진입을 만들지 않는다. `_ExitTree()`가 완료 event를 무조건 발행하지 않는다. **확정.**
2. **Completed handler 예외 격리 -> 권장 채택(subscriber별 try/catch + `GD.PushError`, 나머지 subscriber 계속).**
   Finding 1의 deferred 정리로 세션 정리는 event 전에 이미 끝나므로 한 handler 예외가 세션 상태를 오염시키지
   않는다. **확정.**
3. **AutoStart 호환 seam 위치 -> 권장 채택(U1은 `TurnHelper` 내부 제한 유지).** 별도 legacy bootstrap Node 분리는
   비용 대비 실익이 낮고, 직접 실행/기존 회귀만 이 seam을 소비한다. 모든 소비자 전환 뒤 삭제 후보로 기록. **확정.**
4. **Invalid setup 결과 전달 시점 -> 권장 채택(deferred).** Finding 1에 따라 **모든 완료 경로로 확대**한다
   (사망 구동 완료 포함). `Start()` 콜스택/사망 signal 콜스택 안에서 동기 Completed를 호출하지 않는다. **확정+확대.**
5. **(신규, Finding 5) 동시 PC 사망 + 상대 전멸 우선순위 -> 권장 Defeat 우선.** 사용자 확정 대상. 확정 시 Step 3
   완료 조건에 same-frame mutual kill 케이스를 단언으로 추가한다.

## Step Assessment

- **Step 1 (TurnHelper Explicit Lifecycle Seam)**: 입력=현 `battle_field.tscn` 직접 실행. 출력=`AutoStart` +
  configure/start/stop 상태 + 공개 seed + cursor 수정. **선행 blocker 없음** — 승인 즉시 착수 가능하다. Finding
  1~5는 모두 Step 2/3 완료 계약이라 Step 1을 막지 않는다. cursor 수정은 코드 근거가 명확하고 회귀
  (`sk002_step3`, `cb001_step1~4`)로 관찰 가능. seed 주입은 이미 `ResetCombatRng(long)` 공개 경로가 있어
  reflection 없이 확장 가능. **잘 분해됨.**
- **Step 2 (BattleSession Start and Ownership)**: 입력=Step 1 seam. 출력=request/result 타입 + state machine +
  PackedScene 소유 + 검증 + 참가자 준비/ammo 이관 + 단일 세션 가드. 선행: Finding 4(deferred invalid-setup
  완료). `AddChild` 동기 `_Ready`로 ArticlesContainer dict가 채워지는 전제 확인됨. **착수 가능(Finding 4 반영
  후).**
- **Step 3 (Completion, Result Capture, Sequential Re-entry)**: 가장 무겁다. **선행 Finding 1/2/3/4/5 전부**를
  완료 계약으로 반영해야 단언 가능. deferred teardown, event-identity 기반 캡처, 자체 상대 bookkeeping, latch,
  mutual-kill 우선순위가 여기서 고정된다. 반영 후 헤드리스 검증 강함.
- **Step 4 (Integration Docs + Completion Review)**: 정적 검토 + 문서 반영 + 완료 리뷰. 문제 없음.

Step 분해 자체는 적절하다. seam → ownership → completion → docs가 독립 검증 가능하고, 구조 변경(seam/ownership)과
완료 로직을 Step 사이에 분리한다(워크플로 원칙 7 준수). RNG/FX tick을 U1에서 이동하지 않는 축소 결정도 타당하다.

## Verification Assessment

현 Verification Matrix는 견고하다. Findings 반영으로 **추가/명시해야 할 실패 시나리오**:

- **Deferred 완료 단언(Finding 1/4)**: 사망 감지 프레임에는 teardown/Completed가 아직 없고, 다음 idle frame에
  Completed가 정확히 1회. 사망 signal 콜스택 안에서 `RemoveChild`/`QueueFree`가 실행되지 않음을 관찰.
- **Event-identity 캡처(Finding 2)**: 죽은 유닛의 `FinalHealth == 0`(또는 null) + `Survived == false`가, OnDead
  시점 라이브 HP가 아직 양수여도 올바르게 기록됨. `queue_free` 이후에도 결과 목록 보존.
- **자체 상대 bookkeeping(Finding 3)**: ArticlesContainer dict 제거 순서와 무관하게 Victory 판정.
- **Same-frame mutual kill(Finding 5)**: PC와 마지막 상대가 같은 프레임에 죽을 때 확정 우선순위대로 단일 outcome,
  Completed 1회.
- **반복 실행 상태 격리(코드로 사전 확인됨)**: `resource_local_to_scene = true`(`battle_field.tscn:28` 등)로
  Instantiate마다 HP/상태 복제, SkillAmmoState/StatusController는 순수 C#이라 세션 간 이월 없음 — 두 세션 연속
  실행에서 seed만 다르면 완전 격리. 단언으로 승격 권장.
- **정적 context 정리**: `RemoveChild` → `_ExitTree`가 `_battleFieldScene == this`일 때만 null. 첫 세션 정리 직후
  두 번째 세션이 stale singleton 없이 시작.

필수 회귀 후보(Task Verification Matrix)는 적절하다. `cb001_step4` 로스터 baseline 실패는 [[Open-Tasks]]의
self-sufficient rebaseline Task와 구분해 다룬다는 명시도 정확하다.

## 잘 된 부분

- Task/ADR의 현황 서술이 실측 코드와 정확히 일치한다(cursor 결함, `IsGameOver`의 PC 부재, 정적 context 미정리,
  `ChargeBattleAmmo`/RNG/FX의 `TurnHelper` 결합).
- 책임 분해(BattleSession=수명주기/판정/결과, TurnHelper=턴 순서/실행)가 코드 경계와 정합하고, RNG/FX를 U1에서
  이동하지 않는 축소 범위가 현실적이다.
- 결과를 scene-free 값(`BattleResult`/`BattleUnitResult`)으로 두고 Godot 참조를 유출하지 않는 계약이 반복 실행/
  테스트/정산 소비에 안전하다.
- Godot Variant signal에 C# record를 억지로 넣지 않고 C# event로 결과를 전달하는 판단이 타당하다.

## Verdict

**Approved after design fixes.**

P0 없음. 설계는 실측 코드와 정합하고 Step 분해가 독립 검증 가능하다. 두 건의 P1(Finding 1 deferred 완료, Finding 2
event-identity 캡처)은 이 세션에서 Task/ADR 완료 계약에 접어넣어 해소했다. Step 1 public API(TurnHelper
`AutoStart`/configure/start/stop/seed + cursor 수정)는 확정됐고 착수 blocker가 없다.

**착수 순서**: Step 1은 즉시 착수 가능하다(Finding 1~5는 Step 2/3 게이트). Step 2는 Finding 4(deferred
invalid-setup) 반영 후, Step 3는 Finding 1/2/3/4/5 완료 계약을 모두 반영한 뒤 착수한다. **Open Decision 5(동시
사망 우선순위)만 사용자 확정이 남아 있으며**, 권장(Defeat 우선)으로 진행해도 Step 1/2에는 영향이 없고 Step 3
완료 조건에서 확정하면 된다.

## Related

- [[BS-001-Battle-Session]]
- [[ADR-022-Battle-Session-Lifecycle]]
- [[Turn-System]]
- [[Article-Status-System]]
- [[ADR-017-Deterministic-Combat-Resolution]]
- [[ADR-021-Skill-Ammo-System]]
- [[STEP_REVIEW_WORKFLOW]]
