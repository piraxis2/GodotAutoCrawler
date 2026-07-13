---
id: BS-001
type: task
status: complete
system: Combat
created: 2026-07-13
updated: 2026-07-13
step0: Approved after design fixes ([[BS-001-Battle-Session-Review]])
completion: 완료 ([[BS-001-Battle-Session-Completion-Review]])
tags: [task, combat, battle-session, lifecycle, turn-system]
---

# BS-001 BattleSession Runtime Boundary

## Goal

기존 `battle_field.tscn` 전투를 게임 루프가 명시적인 입력으로 시작하고, 승리 또는 PC 사망 결과를 정확히 한 번 돌려받은 뒤, 같은 프로세스에서 다음 전투를 독립적으로 다시 실행할 수 있는 `BattleSession` 경계를 만든다.

`BattleSession`은 기존 전투 엔진을 대체하지 않는다. `BattleFieldScene`/`TurnHelper`/`Article` 기반 전투의 생성·설정·종료·결과 캡처·정리를 소유하는 런타임 어댑터다.

## Context

### Game design requirement

- 데모 U1은 "전투의 함수화: 시작 API / 종료 판정 = PC 사망 / 결과 구조체"를 요구한다.
- 장기 입력은 `(encounter, modifiers, party snapshot, seed)`지만 U1은 기존 전투 씬을 실행하는 축소 계약으로 시작한다.
- 전투 결과는 U3 등반 루프가 정산한다. `BattleSession`은 주차, 보상, WorldState, UI를 직접 변경하지 않는다.
- 관련 기획: `GameDesign/기획서/08_데모_스코프.md`, `GameDesign/기획서/11_던전데이터_설계.md`.

### Current code facts (2026-07-13)

- `TurnHelper._Ready()`가 seed 초기화, 참가자 수집, 사망 구독, Battle/Expedition ammo 충전, 턴 정렬, 첫 턴 시작을 동시에 수행한다.
- `TurnHelper._PhysicsProcess()`가 턴 실행과 `FxPlayer.Tick()`을 함께 진행한다.
- `TurnHelper.IsGameOver`는 PC가 아니라 턴 참가자 수, 현재 턴 유닛, `Ally`/`Opponent` 목록 공백으로 종료를 판단하고, 종료 시 TODO에서 조용히 멈춘다.
- 사망한 현재 턴 유닛은 목록에서 먼저 제거되며, `IndexOf(current) == -1` 뒤 `(index + 1) % count`가 0이 되어 턴 순환이 목록 처음으로 리셋될 수 있다.
- `BattleFieldScene.BattleField`는 정적 전역 컨텍스트이고 `_ExitTree()` 정리가 없다. 기존 테스트는 씬 제거 후 reflection으로 정적 필드를 비운다.
- `battle_field.tscn`은 참가자와 `TurnHelper`를 직접 포함한다. `_combatSeed`는 export 기본값 1이며 테스트가 reflection으로 바꾼다.
- CB-001 결정론 테스트는 전투 씬을 반복 인스턴스화하고 제거하는 선례가 있지만, 제품용 시작/완료 API는 없다.

코드와 문서가 다르면 위 설명이 아니라 실제 코드와 실행 결과를 최종 사실로 사용한다.

## Approved Direction

사용자 논의에서 다음 방향을 승인했다.

1. `BattleSession`이 전투 씬을 직접 생성하고 소유한다.
2. U1부터 지정된 PC 사망을 패배 조건으로 사용한다.
3. 최종 입력 계약은 encounter/modifier/party snapshot 확장 자리를 보존하되, Step 1 구현은 `PackedScene + seed + PC NodePath` 축소 입력으로 시작한다.
4. `TurnHelper`는 최종적으로 턴 순서와 턴 실행만 소유한다. 전투 수명 주기, 승패, 참가자 준비, 결과 수집은 바깥으로 이동한다.
5. RNG와 FX tick은 U1에서 `TurnHelper`에 임시 유지하고, 순수 시뮬레이션/프레젠테이션 분리 때 이동한다.

장기 설계 판단은 [[ADR-022-Battle-Session-Lifecycle]] 초안에 분리한다. 구현 전 Step 0 설계 리뷰에서 승인 또는 수정한다.

## Scope

- `BattleSession : Node`와 명시적 `Configure`/`Start`/완료 이벤트 수명 주기.
- 축소형 `BattleRequest`, `BattleResult`, outcome/end-reason, 유닛 결과 계약.
- `BattleSession`의 전투 씬 직접 생성·소유·정리.
- 지정 PC 사망=`Defeat`, 상대 생존자 0=`Victory` 판정.
- 잘못된 씬/경로/진영/초기 편성을 `Aborted/InvalidSetup`으로 fail-closed.
- `TurnHelper`의 명시적 configure/start/stop 상태와 기존 직접 실행 호환 seam.
- 참가자 준비 및 ammo 충전을 세션 시작 준비 단계로 이동하는 신규 경로.
- 현재 턴 유닛 사망 시 순환이 처음으로 리셋되지 않는 커서 수정.
- `BattleFieldScene.BattleField`의 `_ExitTree()` 정리와 동시 세션 거부.
- 사망 전에 참가자 상태를 기록하고 완료 결과를 정확히 한 번 반환.
- 같은 프로세스에서 세션 2회 연속 실행 검증.
- CB-001/SK-001/SK-002/ST-001 관련 전투 회귀 보존.

## Out of Scope

- 순수 `TurnResolver`와 프레젠테이션 계층 분리.
- `CombatRng`를 `BattleContext`로 이동.
- `FxPlayer.Tick()`을 `TurnHelper`에서 분리.
- `BattleRoster`, `BattleRecorder`, `BattleRules`의 선제적 개별 클래스 추출. U1에서는 책임 경계만 고정하고 실제 중복이 생길 때 추출한다.
- 명시적 Faction 타입 도입 및 부모 노드 이름(`Ally`/`Opponent`) 규약 제거.
- `EncounterDefinition`, `EncounterModifier`, `PartySnapshot`, `DungeonDefinition` 실제 구현.
- 전투 보상, XP, 주차, WorldState, SaveGame, GameLog, Workspace 자동 전환.
- 다층 등반, Expedition ammo 이월, 한계 턴, 수동 후퇴.
- 동료 영구 사망의 로스터 반영.
- 리플레이 저장 포맷, 모의전 10판 병렬 실행, 동시 BattleSession.
- 전투 중 저장.

## Proposed Contracts

### BattleRequest v0

```csharp
public sealed record BattleRequest
{
    public required PackedScene BattleScene { get; init; }
    public required long Seed { get; init; }
    public required NodePath PlayerPath { get; init; }
}
```

- `PlayerPath`는 U1 임시 계약이며 전투 씬 루트 기준이다(기존 씬 예: `Articles/Ally/Character`).
- 장기적으로 `PartySnapshot`의 안정적인 `unit_id`로 대체한다.
- 새 전투 seed 생성 정책은 호출자 소유다. `BattleSession`은 받은 seed를 사용하고 결과에 보존한다.

### Result enums

```csharp
public enum BattleOutcome
{
    Victory,
    Defeat,
    Retreat,
    Aborted,
}

public enum BattleEndReason
{
    OpponentsEliminated,
    PlayerDefeated,
    RoundLimit,
    ManualRetreat,
    InvalidSetup,
    SceneRemoved,
}
```

- U1에서 실제 정상 종료로 발생시키는 조합은 `Victory/OpponentsEliminated`, `Defeat/PlayerDefeated`다.
- `Retreat`, `RoundLimit`, `ManualRetreat`는 타입 자리만 확보하고 동작은 후속이다.
- 설정 실패는 게임 패배가 아니라 `Aborted/InvalidSetup`이다.

### BattleResult v0

```csharp
public sealed record BattleResult
{
    public required BattleOutcome Outcome { get; init; }
    public required BattleEndReason EndReason { get; init; }
    public required long Seed { get; init; }
    public required bool PlayerAlive { get; init; }
    public required int SurvivingAllies { get; init; }
    public required int SurvivingOpponents { get; init; }
    public required IReadOnlyList<BattleUnitResult> Units { get; init; }
}

public sealed record BattleUnitResult
{
    public required string SessionUnitId { get; init; }
    public required string Faction { get; init; }
    public required bool Survived { get; init; }
    public int? FinalHealth { get; init; }
    public required Vector2I FinalTile { get; init; }
}
```

- `SessionUnitId`는 전투 한 번 안에서만 유효하며 v0는 `Faction + SpawnIndex`로 만든다.
- `FinalHealth`가 nullable인 이유는 현재 `Health`가 없는 Article도 존재할 수 있기 때문이다.
- 표시 이름, NodePath, Godot 객체 참조는 결과의 안정 식별자로 사용하지 않는다.
- 보상·XP·WorldState 변경은 결과에 넣지 않는다.
- **사망 유닛의 `Survived`/`FinalHealth`는 `OnDead` 이벤트 identity를 진실로 취급한다(Step 0 Finding 2).**
  `Health.CurrentHealth` setter가 `_currentHealth`를 0으로 clamp하기 **전에** `Owner.Dead()`(→ OnDead)를
  호출하므로(`Health.cs:22~24`, `ArticleStatus.HasLivingHealth`는 `CurrentHealth > 0`), OnDead 콜백 시점의 라이브
  HP/`IsAlive`는 아직 사망 전 값이다. 따라서 OnDead를 올린 유닛은 라이브 재조회와 무관하게 `Survived=false`,
  `FinalHealth=0`(또는 null)로 기록한다. 승패 판정도 죽은 유닛의 `IsAlive` 재스캔이 아니라 이벤트 identity로 한다.

### BattleSession API and state

```csharp
var session = new BattleSession();
session.Configure(request);
session.Completed += OnBattleCompleted;
host.AddChild(session);
session.Start();
```

```text
Created → Configured → Running → Completed → Disposed
```

- `Configure`는 SceneTree 진입 전 호출한다.
- `Start`는 자동 `_Ready()` 시작이 아니라 명시적 호출이다.
- `Completed`는 `Action<BattleResult>` C# event로 결과를 정확히 한 번 전달한다. Godot Variant signal에 plain C# record를 억지로 넣지 않는다.
- configure 전 start, 중복 configure/start, completed 뒤 start는 fail-closed다.
- 완료 결과는 전투 씬과 정적 컨텍스트 정리가 끝난 뒤 발생한다.

## Target Responsibility Split

| Responsibility | U1 owner |
|---|---|
| 전투 씬 생성/정리 | `BattleSession` |
| 입력 검증/PC 해석 | `BattleSession` |
| 승패 판정 | `BattleSession` |
| 결과 및 사망 기록 | `BattleSession` 내부, 후속 필요 시 `BattleRecorder` 추출 |
| 참가자 registry/faction 조회 | 기존 `ArticlesContainer` 공개 API |
| 전투 시작 참가자 준비/ammo | `BattleSession` 준비 단계 + 참가자 hook |
| 턴 순서/다음 행동자/TurnPlay | `TurnHelper` |
| 턴 시작 효과 호출 | `TurnHelper` |
| RNG | U1은 `TurnHelper` 임시 유지 |
| FX tick | U1은 `TurnHelper` 임시 유지 |
| 보상/XP/WorldState/다음 층 | U3 게임·등반 루프 |

### TurnHelper target after U1

```text
받은 참가자 목록 정렬
  → 다음 행동자 선택
  → 턴 시작 효과 1회
  → TurnPlay 실행
  → Success/Failure이면 다음 행동자로 이동
  → 외부 Stop 요청 시 정지
```

U1에서 제거하거나 신규 세션 경로 밖으로 격리할 책임:

- 승패/게임오버 의미 판정.
- 결과 생성.
- 신규 세션 경로의 참가자 발견.
- 신규 세션 경로의 ammo reset.

기존 직접 씬 실행과 회귀 테스트를 위해 `AutoStart=true` 호환 경로를 일시 유지할 수 있다. 신규 제품 경로는 반드시 `AutoStart=false` + 명시적 `Configure/StartBattle`을 사용한다. 이 호환 경로의 최종 제거는 모든 직접 실행 소비자 전환 뒤 후속으로 판단한다.

## Lifecycle and Completion Order

### Start

```text
BattleSession.Start
  → request/state/동시 세션 검증
  → PackedScene instantiate
  → TurnHelper AutoStart=false 설정
  → 전투 씬을 Session 자식으로 AddChild
  → ArticlesContainer ready 결과에서 참가자와 PC 해석
  → 초기 참가자 snapshot 및 OnDead 구독
  → 참가자 전투 시작 준비(ammo 포함)
  → TurnHelper.Configure(participants, seed)
  → TurnHelper.StartBattle
```

### Completion

```text
사망/진영 상태 변화(OnDead)
  → 완료 latch 동기 확인·설정 (같은 프레임 재진입 no-op)
  → PC 사망 또는 상대 생존자 0 판정 (event identity 기반)
  → BattleResult 최종 캡처 (사망 유닛은 event identity로 기록)
  → 이하 teardown+event를 CallDeferred로 한 단위로 idle frame에 실행:
      → TurnHelper.StopBattle
      → signal/event 구독 해제
      → BattleFieldScene을 Session에서 RemoveChild
      → BattleFieldScene._ExitTree가 정적 context 정리
      → QueueFree
      → Completed event 1회 발생
```

- **완료 감지 즉시 latch만 동기 설정하고, teardown+event 블록 전체는 deferred로 실행한다(Step 0 Finding 1/4,
  OD4 확대).** 사망은 `TurnHelper._PhysicsProcess` → `TurnPlay` → `Health` setter → `Owner.Dead()` → OnDead
  콜스택 안에서 발생하므로, 그 스택에서 동기 `RemoveChild`/`QueueFree`하면 처리 중 서브트리를 제거하는 위험과
  Completed 핸들러의 즉시 session2 재시작으로 인한 stale static 충돌이 생긴다.
- 완료 latch는 첫 완료 감지에서 동기 설정해 같은 프레임의 두 번째 OnDead(광역/동시 사망)를 no-op으로 만든다.
- 상대 전멸 판정은 세션이 Start에서 추적한 상대 집합의 자체 bookkeeping으로 하고, `ArticlesContainer.Articles`
  dict 제거 순서에 의존하지 않는다(Step 0 Finding 3).
- 사망 유닛은 `queue_free()`되기 전에 세션 기록에 보존한다.
- deferred 블록 안에서 `RemoveChild`로 `_ExitTree()` 정리를 동기적으로 유도한 뒤 완료 event를 발생시켜, 완료
  핸들러가 시작하는 다음 세션이 stale static context와 충돌하지 않게 한다.
- `BattleFieldScene._ExitTree()`는 `_battleFieldScene == this`일 때만 null로 만든다.
- v0는 정적 `BattleFieldScene.BattleField` 제약 때문에 동시에 하나의 세션만 허용한다. 두 번째 시작은 `Aborted/InvalidSetup`으로 닫는다.
- `run/main_scene`은 `workspace.tscn`이고 `battle_field.tscn`이 아니므로 부팅 시 정적 context는 비어 있다. U1
  test/host가 static을 소유한다. workspace 임베드 통합은 후속(Follow-ups).

## Failure Policy

- PackedScene null/instantiate 실패, 필수 `BattleFieldScene`/`TurnHelper`/`ArticlesContainer` 없음, PC path miss, PC가 Ally가 아님, 초기 Ally/Opponent 공백은 `Aborted/InvalidSetup` 결과를 정확히 한 번 반환한다.
- 제품 구성 오류를 일반적인 `Defeat`로 위장하지 않는다.
- `Completed` handler 예외가 세션 내부 정리나 다른 handler 호출을 방해하지 않도록 event 호출 정책을 설계 리뷰에서 확인한다.
- 세션이 Running 중 외부에서 tree에서 제거되면 가능한 범위에서 `Aborted/SceneRemoved`를 기록하되, `_ExitTree()`에서 완료 event를 무조건 발행해 재진입을 만들지는 않는다. 이 경로의 정확한 정책은 Step 0 Open Decision이다.
- 기존 `BattleFieldScene.BattleField`가 다른 유효 인스턴스를 가리키면 덮어쓰지 않고 시작을 거부한다.

## Steps

### Step 0 — Design Review + ADR

Goal:

- Task와 [[ADR-022-Battle-Session-Lifecycle]]을 실제 코드와 대조해 구현 가능성을 승인한다.

Scope:

- request/result 타입, Node 수명 주기, 완료 순서, 정적 context 정책, TurnHelper 호환 seam을 검토한다.
- 아래 Open Decisions를 확정한다.
- 제품 코드와 리소스는 수정하지 않는다.

Done condition:

- 리뷰 결과 `Approved` 또는 설계 수정 반영 후 `Approved after design fixes`.
- P0/P1 설계 문제가 없고 Step 1 public API가 확정됨.

판정: **Approved after design fixes** ([[BS-001-Battle-Session-Review]], 2026-07-13). P0 없음. 두 P1
(deferred 완료, event-identity 캡처)은 위 Failure Policy/Lifecycle/Contracts/Open Decisions에 반영 완료.
Step 1 public API(TurnHelper `AutoStart`/configure/start/stop/seed + cursor 수정) 확정, 착수 blocker 없음.
Open Decision 5(동시 사망 우선순위)는 Defeat 우선으로 사용자 확정(2026-07-13). 모든 설계 결정 해소됨.

### Step 1 — TurnHelper Explicit Lifecycle Seam

Goal:

- 기존 직접 씬 실행을 보존하면서 신규 Session이 전투 시작 시점을 제어할 수 있다.

Scope:

- `TurnHelper`에 `AutoStart`, configure/start/stop 상태와 공개 seed 설정을 추가한다.
- 명시적 참가자 입력 경로와 중복 시작/정지 방어를 추가한다.
- 현재 턴 유닛 사망 시 다음 순서가 목록 처음으로 리셋되는 커서 결함을 수정한다.
- RNG와 FX tick은 이동하지 않는다.

Done condition:

- `AutoStart=true` 기존 동작 회귀 유지.
- `AutoStart=false`에서 physics frame이 지나도 턴이 시작되지 않음.
- configure→start 후에만 턴 진행.
- start/stop 중복 호출이 안전함.
- 현재 턴 유닛 제거 후 정규 다음 유닛이 선택됨.

Verification:

- 전용 headless lifecycle/cursor 테스트.
- CB-001 Step 1~4, SK-002 reset 회귀.
- `dotnet build`, Godot `--import`.

코드 리뷰(2026-07-13): **수정 후 완료.** P2 2건 + P3 1건 반영 완료:

- [P2] Configure 재호출 OnDead 누적 → handler를 `_deathSubscriptions`에 보관하고 재구성/StopBattle 시
  `IsInstanceValid` 가드로 해제. 재구성 무누적 + 정지 후 구독 0 테스트 추가(`BR.*`).
- [P2] 명시 경로 진영 공백 검사 → `IsGameOver`의 faction 공백 판정을 `AutoStart`(legacy) 경로로 한정. 명시
  경로는 참가자 링 진행 가능 여부만 판단(ADR-022 §3 경계 준수).
- [P3] ADR OD5 stale → [[ADR-022-Battle-Session-Lifecycle]] Status를 "Defeat 우선 사용자 확정"으로 갱신.

구현 결과(2026-07-13):

- 변경: `Assets/Script/TurnHelper.cs`에 `RunState`(Idle/Configured/Running/Stopped) + `[Export] AutoStart`(기본 true) +
  공개 `Configure(participants, seed)`/`StartBattle`/`StopBattle` 추가. `_Ready`는 AutoStart일 때만 컨테이너 참가자
  수집·ammo 충전 후 Configure→StartBattle. `_PhysicsProcess`는 `Running`에서만 턴 진행(FX tick 포함). 정수 커서
  (`_turnCursor`/`_currentUnitRemoved`)로 현재 유닛 사망 시 후임 선택(목록 처음 리셋 결함 제거). `IsGameOver`는
  컨테이너 null-safe. RNG/FX는 이동하지 않음(범위 유지).
- 신규 테스트: `bs001_step1_lifecycle_test`(23 assert ALL PASS) — AutoStart on/off, Configured 미진행, Start/Stop
  멱등, Stop 정지, 커서 후임/wrap/선행제거.
- 적응: `Cb001Step1TurnEffectTest.MakeHelper`가 화이트박스로 `_PhysicsProcess`를 직접 구동하므로 reflection으로
  `_runState=Running` 세팅 추가(턴 효과 단언 불변). 제품 동작 변경 아님.
- 검증: `dotnet build` 경고/오류 0, `--import` OK. 회귀 PASS — cb001_step1/2/3, sk002_step1/3, st001_step1,
  bs001_step1. **선행 실패(내 변경과 무관, clean-baseline 동일 재현):** cb001_step4/sk001_step1/sk001_step3는
  로스터 개편으로 `Articles/Opponent/Character`(현 `Character2`) 셋업 NPE — rebaseline Task 소관([[Open-Tasks]]).

### Step 2 — BattleSession Start and Ownership

Goal:

- `BattleRequest`로 기존 전투 씬을 생성하고 명시적으로 시작할 수 있다.

Scope:

- request/result 기본 타입과 `BattleSession` state machine 추가.
- PackedScene 생성, 필수 노드/PC/초기 진영 검증.
- 전투 씬 직접 소유, 단일 활성 세션 가드.
- 참가자 조회를 위한 `ArticlesContainer` 공개 API 추가.
- 신규 세션 경로의 참가자 준비 및 ammo 충전을 `TurnHelper` 밖으로 이동.
- reflection 없이 seed 주입.

Done condition:

- 유효 request가 전투를 시작함.
- invalid request matrix가 crash/SCRIPT ERROR 없이 `Aborted/InvalidSetup` 1회 반환.
- 직접 실행 호환 경로와 Session 경로가 모두 존재하되 신규 경로는 자동 시작을 사용하지 않음.

코드 리뷰(2026-07-13): **수정 후 완료.** P2 3건 반영:

- [P2] PC의 Ally 검증이 부모 이름만 확인 → `articles.Articles["Ally"].Contains(player)` 등록 여부 +
  `ITurnAffectedArticle<ArticleBase>` 참가자 여부로 강화. 컨테이너 밖 위조 "Ally" 노드를 거부(Step 3 사망 추적
  정합). fixture `bs001_step2_forged_ally.tscn` + `F_forged_ally` 케이스 + `Fc` 대조(실 PC는 정상 시작).
- [P2] invalid matrix 불완전 → instantiate-fail(빈 PackedScene), 필수 TurnHelper 누락, 필수 ArticlesContainer
  누락 fixture/케이스 추가(`B6`/`B7`/`B8`) — deferred 1회 완료 확인.
- [P2] Completed 예외 격리 미검증 → 첫 subscriber throw + 두 번째 수신 테스트 추가(`E`, OD2 계약).
- [P2 재리뷰] 빈/손상 PackedScene에 `Instantiate()` 호출이 Godot 엔진 ERROR(`Failed to instantiate scene state`)를
  남겨 로그/CI 오탐 → `Instantiate()` 전에 `CanInstantiate()` 선검사로 ERROR 없이 `Aborted/InvalidSetup` 종료.
  `B6`은 엔진 ERROR 미출력으로 재확인.

구현 결과(2026-07-13):

- 신규 `Assets/Script/Battle/`: `BattleOutcome`/`BattleEndReason` enum, `BattleRequest`/`BattleUnitResult`/
  `BattleResult` record(`required`, scene-free 값), `BattleSession : Node`(state machine
  Created/Configured/Running/Completed).
- `BattleSession.Start`: 단일 세션 가드(`IsInstanceValid(BattleField)`) → `BattleScene` null → instantiate 타입 →
  필수 노드(TurnHelper/Articles) → AutoStart=false 후 AddChild → 초기 진영 공백 → PC 해석(Ally 소속). 검증 통과
  시 세션이 참가자 준비 + ammo 충전(TurnHelper 밖) 후 `TurnHelper.Configure(participants, seed)`+`StartBattle`.
  실패는 전부 `Aborted/InvalidSetup`을 deferred(`Callable.From(...).CallDeferred()`)로 1회 완료(OD4), 완료 latch는
  동기 설정. `Completed`는 subscriber별 try/catch 격리(OD2).
- `ArticlesContainer.GetTurnParticipants()` 공개 API 추가. seed는 `Configure`로 reflection 없이 주입.
- 신규 테스트: `bs001_step2_session_start_test`(79 assert ALL PASS) — 유효 시작(소유/Running/seed/세션 ammo),
  invalid matrix 9종(null/wrong-type/instantiate-fail/missing-TurnHelper/missing-Articles/PC miss/PC not Ally/
  forged-Ally/empty factions) deferred 1회, forged 대조, 단일 세션 가드, lifecycle fail-closed, Completed 예외
  격리. fixtures: `bs001_step2_empty_factions`/`_missing_turnhelper`/`_missing_articles`/`_forged_ally`.tscn.
- 범위 밖(Step 3): 승패 판정·결과 캡처·`_ExitTree` 정적 정리·연속 재실행. Step 2 abort는 씬 QueueFree + 가드
  `IsInstanceValid`로 닫고, 정적 context null 정리는 Step 3 소관.
- 검증: `dotnet build` 경고/오류 0, `--import` OK. 회귀 PASS — bs001_step1, cb001_step1/2/3, sk002_step1/3,
  st001_step1. 선행 실패(cb001_step4/sk001_step1/3)는 로스터 rebaseline 소관으로 불변.

### Step 3 — Completion, Result Capture, Sequential Re-entry

Goal:

- 승리/PC 사망 결과를 완결된 값으로 반환하고 정리 후 즉시 다음 전투를 실행할 수 있다.

Scope:

- 참가자 초기 기록과 death 구독.
- PC 사망/상대 전멸 판정.
- result unit snapshot, completed latch, 구독 해제, 씬/정적 context 정리.
- `BattleFieldScene._ExitTree()` 안전 정리.
- 같은 프로세스 2회 연속 실행.

Done condition:

- 적 전멸=`Victory/OpponentsEliminated`.
- 지정 PC 사망=`Defeat/PlayerDefeated`(다른 Ally 생존 여부와 무관).
- 동시 PC 사망 + 상대 전멸(mutual kill)=`Defeat/PlayerDefeated`(OD5 확정, PC 사망을 먼저 평가).
- Completed event가 각 세션당 정확히 1회.
- 사망 유닛이 queue_free된 뒤에도 결과 목록과 사망/최종 상태가 보존됨.
- 첫 세션 정리 직후 두 번째 세션이 stale singleton, signal 누적, ammo/RNG/참가자 오염 없이 완료됨.

코드 리뷰(2026-07-13): **미완료 → 수정 후 재검증.** P1 1건 반영:

- [P1] 첫 턴 시작 효과 사망이 완료 예약 누락 → `StartBattle()`(첫 행동자 `ApplyTurnStartEffects` 동기 실행)이
  외부 코드로 PC/마지막 상대를 죽일 수 있는데, 그 시점 `_state`가 아직 Configured라 `OnParticipantDead`가
  `_state != Running`으로 조기 반환 → 완료 영구 누락. 수정: `TryBeginBattle`이 `StartBattle` **직전에**
  `_state=Running`으로 전환(Start()의 사후 설정 제거). lethal HealthRegen fixture `bs001_step3_lethal_start.tscn`
  + `H` 케이스(deferred·1회·정적 정리·PC 사망) 추가.

구현 결과(2026-07-13):

- `BattleSession`: `TrackedUnit`(SessionUnitId=Faction:SpawnIndex, Faction, IsPlayer/IsOpponent, Dead,
  FinalHealth/FinalTile, OnDeadHandler) 참가자 추적을 StartBattle 전에 배선. `OnParticipantDead`는 사망 콜스택에서
  **동기적으로 tracking 갱신 + 완료 latch(`_completionPending`)만** 설정하고 `CompleteBattleDeferred`를 한 번
  `CallDeferred`(Finding 1). deferred가 같은 프레임 모든 사망 뒤 outcome 판정 → PC 사망 먼저 평가(OD5 mutual
  kill), 그 뒤 결과 캡처·`TurnHelper.StopBattle`·구독 해제·`RemoveChild`(→`_ExitTree` 정적 null)·`QueueFree`·
  `Completed` 1회.
- 사망 캡처는 event-identity(Finding 2): `Health` setter가 clamp 전에 `Dead()`를 emit하므로 라이브 HP를 읽지
  않고 사망 유닛을 `Survived=false`/`FinalHealth=0`로 기록. 상대 전멸 판정은 세션 자체 `_aliveOpponents`
  bookkeeping(Finding 3, dict 제거 순서 비의존).
- `BattleFieldScene._ExitTree()`: `_battleFieldScene==this`일 때만 null(ADR-022 §7). abort/완료 teardown이
  `RemoveChild`로 동기 정리 유도 → 완료 handler의 즉시 재진입이 stale singleton과 충돌하지 않음.
- 신규 테스트: `bs001_step3_completion_test`(45 assert ALL PASS) — Victory(deferred/1회/결과 보존/정적 정리),
  Defeat(단일/생존 Ally), mutual kill Defeat 우선, 첫 턴 lethal 효과 완료(리뷰 P1), 2회 연속 재진입(handler 내
  시작·seed/상태 격리). fixtures `bs001_step3_two_allies`/`bs001_step3_lethal_start`.tscn.
- 검증: `dotnet build` 경고/오류 0, `--import` OK. **회귀 PASS(중요: `_ExitTree`가 legacy 경로/기존 테스트에
  영향):** bs001_step1/2, cb001_step1/2/3, sk002_step1/3, st001_step1 — 전부 instantiate+free하며 통과. 선행
  실패(cb001_step4/sk001_step1/3)는 로스터 rebaseline 소관으로 불변.

### Step 4 — Integration Documentation and Completion Review

Goal:

- 현재 사실과 소비 계약을 Wiki에 반영하고 U3가 안전하게 의존할 수 있음을 판정한다.

Scope:

- [[Turn-System]]에 Session/TurnHelper 책임과 public entry point 반영.
- 신규 BattleSession 시스템 문서가 필요하면 `20_Systems/Battle-Session-System.md` 작성.
- [[Current-State]], [[Open-Tasks]], 본 Task 갱신.
- `50_Reviews/BS-001-Battle-Session-Completion-Review.md` 완료 리뷰(설계 리뷰 `BS-001-Battle-Session-Review.md`와 별개).

Done condition:

- P0/P1 없음, BS-001 신규·영향권 회귀 통과, 문서와 코드 일치. (필수 후보 중 `cb001_step4`/`sk001_step1`/
  `sk001_step3`은 로스터 개편 선행 실패로 clean-baseline 동등성 확인 후 rebaseline Task로 분리 — BS-001 회귀 아님.
  즉 문자 그대로의 "전체 통과"는 아니며 선행 실패 3종은 별도 소관이다.)
- U3가 `BattleRequest`를 만들고 `BattleResult`를 소비할 수 있는 계약이 고정됨.

구현 결과(2026-07-13, **제품 코드 변경 없음** — 문서/완료 리뷰만):

- 신규 [[Battle-Session-System]](`20_Systems/Battle-Session-System.md`): 소비 계약·state machine·outcome·검증
  순서·완료 순서(event-identity+deferred)·단일 세션·재실행 격리·후속을 보존.
- [[Turn-System]] 갱신: lifecycle seam(`AutoStart`/configure/start/stop), 커서 결함 수정, 승패 소유권 이동,
  `bs001_step1` 검증.
- [[Current-State]] Combat에 BS-001 전체 완료 추가, [[Open-Tasks]] BS-001을 Recently Completed로 이동 + 후속 분리.
- 완료 리뷰 [[BS-001-Battle-Session-Completion-Review]](Step 0~4 대조, Verification Matrix, 판정: 완료).
- 검증 재확인: `dotnet build` 경고/오류 0, `--import` exit 0, `bs001_step1`(27)/`bs001_step2`(79)/
  `bs001_step3`(45) ALL PASS, 회귀 cb001_step1~3·sk002_step1/3·st001_step1 ALL PASS.

## Completion Criteria

- 게임 루프가 `BattleRequest`를 통해 전투를 명시적으로 시작할 수 있다.
- `BattleSession`이 전투 씬의 생성부터 정리까지 소유한다.
- PC 사망은 다른 아군 생존 여부와 무관하게 패배다.
- 상대 전멸은 승리다.
- 잘못된 구성은 crash나 일반 패배가 아니라 `Aborted/InvalidSetup`이다.
- 결과는 seed, outcome/reason, 생존 수, 참가자별 생존/최종 HP/위치를 보존한다.
- 결과에 Godot 객체 참조가 남지 않는다.
- 완료 이벤트는 한 세션당 정확히 한 번이다.
- 같은 프로세스에서 전투 2회가 독립적으로 완료된다.
- `TurnHelper` 신규 경로는 승패, 결과, 참가자 준비를 소유하지 않는다.
- 현재 턴 유닛 사망 시 턴 순서가 목록 처음으로 부당하게 리셋되지 않는다.
- 기존 결정론, 스킬, 상태 시작 회귀가 유지된다.
- 제품 코드에는 WorldState/SaveGame/Workspace/Dungeon 직접 의존이 추가되지 않는다.

## Verification Matrix

필수 신규 검증:

- `AutoStart=true/false`, configure/start/stop, 중복 호출.
- 현재 턴 유닛 제거 시 cursor 보정.
- request validation failure matrix.
- Victory 결과와 unit snapshot.
- PC 사망 + 다른 Ally 생존 상태의 Defeat.
- 사망/진영 공백이 같은 frame에 발생해도 Completed 1회.
- scene remove/static cleanup.
- seed가 다른 두 세션 연속 실행과 상태 격리.

필수 회귀 후보:

- `cb001_step1_turn_effect_test`
- `cb001_step2_combat_rng_test`
- `cb001_step3_canonical_order_test`
- `cb001_step4_determinism_test`(현재 알려진 로스터 baseline 실패는 self-sufficient rebaseline Task 상태와 구분)
- `sk001_step1_skill_system_test`
- `sk001_step3_mana_hit_test`
- `sk002_step1_ammo_test`
- `sk002_step3_reset_test`
- `st001_step1_natural_regen_test`
- `dotnet build AutoCrawler.sln -c Debug`
- Godot headless `--import`

## Open Decisions for Step 0

Step 0 설계 리뷰([[BS-001-Battle-Session-Review]])에서 아래 1~4를 권장안으로 확정했고, 리뷰 Finding 5로 신규
결정 5를 추가했다.

1. **Running 중 외부 tree removal 정책 — 확정: 내부 정리만, C# event 미발행.**
   - 소유자가 제거 중인 객체에서 콜백 재진입을 만들지 않는다. `_ExitTree()`는 완료 event를 무조건 발행하지 않는다.
   - (기각 대안: `Aborted/SceneRemoved` 완료 event 발행.)
2. **Completed handler 예외 격리 — 확정: subscriber별 try/catch + `GD.PushError`, 나머지 subscriber 계속.**
   - deferred 완료(결정 4)로 세션 정리가 event 전에 끝나므로 한 handler 예외가 세션 상태를 오염시키지 않는다.
   - (기각 대안: 일반 C# event 호출 semantics 유지.)
3. **AutoStart 호환 seam 위치 — 확정: U1은 `TurnHelper` 내부 제한 유지.**
   - 직접 실행/기존 테스트만 소비. 모든 소비자 전환 뒤 삭제 후보로 기록한다.
   - (기각 대안: 즉시 별도 legacy bootstrap Node로 분리.)
4. **완료 결과 전달 시점 — 확정: deferred, 그리고 invalid-setup뿐 아니라 모든 완료 경로로 확대.**
   - Finding 1: 사망 구동 완료 teardown은 `TurnHelper._PhysicsProcess`/`OnDead` 콜스택 안에서 실행하면 tree
     수정/재진입 위험이 있다(`Health.CurrentHealth` setter → `Owner.Dead()` → OnDead가 물리 처리 중 자손에서 발생).
   - 완료 감지 즉시 완료 latch만 동기 설정하고, teardown+event 블록 전체를 `CallDeferred`로 한 단위로 idle
     frame에 실행한다. `Start()` 스택/사망 signal 스택 안에서 동기 Completed를 호출하지 않는다.
   - (기각 대안: 즉시 event.)
5. **(신규) 동시 PC 사망 + 상대 전멸 outcome 우선순위 — 확정: Defeat 우선(`Defeat/PlayerDefeated`).**
   - 같은 프레임에 PC가 마지막 상대를 죽이며 함께 죽는 mutual kill 시 outcome이 결정적이어야 한다(CB-001 결정론).
   - 근거(사용자 확정 2026-07-13): 기획 최상위 패배 규칙은 `PC 사망 = 즉시 세션 패배`다. 자폭성 마지막 공격을
     승리로 인정하면 PC 생존을 관리하는 택틱 보드의 가치가 약해지고, 침공·용의 강림처럼 PC 사망이 배드 엔딩인
     콘텐츠와도 어긋난다. 특수 콘텐츠의 다른 취급은 향후 `BattleRules`가 명시적으로 오버라이드한다.
   - Step 3 판정 순서 고정: (1) 완료 latch 동기 설정 → (2) 지정 PC 사망 여부 확인 → Defeat/PlayerDefeated →
     (3) 상대 생존자 0 확인 → Victory/OpponentsEliminated. Step 3 완료 조건에 mutual kill 단언 추가. Step 1/2 영향
     없음.

## Decisions

- 사용자가 승인한 방향 3건: Session scene ownership, PC-specific defeat, 축소 request부터 시작.
- [[ADR-022-Battle-Session-Lifecycle]] 초안 작성 → Step 0 리뷰에서 `accepted`.
- 순수 시뮬레이션/프레젠테이션 분리는 BS-001 범위 밖이다.
- **Step 0 설계 리뷰(2026-07-13, [[BS-001-Battle-Session-Review]]): Approved after design fixes.** Open
  Decisions 1~4를 권장안으로 확정, Finding 5로 신규 결정 5(동시 사망 우선순위, 권장 Defeat) 추가. P1 2건(deferred
  전 완료 경로 확대, event-identity 캡처)을 Task에 반영.

## Follow-ups

- `EncounterDefinition`/`EncounterModifier`/`PartySnapshot`과 request 확장.
- DungeonRun/U3의 층 순회, HP·마나·Expedition ammo 이월과 정산.
- 한계 턴과 수동 후퇴.
- 명시적 Faction과 `BattleRoster`.
- `CombatContext`/`CombatRng` 이동, `BattleRecorder` 추출.
- 전투 이벤트 스트림과 GameLog kill/death live 배선.
- 리플레이 입력 snapshot과 순수 시뮬레이션/프레젠테이션 분리.
- 모든 직접 씬 실행 소비자 전환 뒤 `AutoStart` legacy seam 삭제 검토.
- U3 workspace World 창 안 전투 씬 임베드 시 정적 `BattleFieldScene.BattleField` 소유권/단일 세션 가드 정합
  (Step 0 Finding 6, WS-002 후속 "전장 scene 연결"과 함께).

## Related

- [[Battle-Session-System]]
- [[BS-001-Battle-Session-Review]]
- [[BS-001-Battle-Session-Completion-Review]]
- [[Turn-System]]
- [[Article-Status-System]]
- [[BehaviorTree-System]]
- [[Skill-System]]
- [[ADR-017-Deterministic-Combat-Resolution]]
- [[ADR-021-Skill-Ammo-System]]
- [[ADR-022-Battle-Session-Lifecycle]]
- [[STEP_REVIEW_WORKFLOW]]

