---
type: system
system: Combat
status: active
updated: 2026-07-13
---

# Battle Session System

## Agent Brief

- 주요 파일: `Assets/Script/Battle/BattleSession.cs`, `BattleRequest.cs`, `BattleResult.cs`,
  `BattleUnitResult.cs`, `BattleOutcome.cs`, `BattleEndReason.cs`; 협력: `Assets/Script/TurnHelper.cs`,
  `Assets/Script/BattleFieldScene.cs`, `Assets/Script/ArticlesContainer.cs`
- 책임: 전투 한 번의 수명 주기 소유 — `BattleRequest`로 전투 씬 생성·소유, 입력 검증, 시작, 승패 판정, 값 결과
  (`BattleResult`) 1회 반환, 씬·정적 context 정리, 같은 프로세스 재실행.
- 소유하지 않음: 턴 순서/실행(`TurnHelper`), 보상·XP·주차·WorldState·SaveGame·UI 전환(호출자/U3 등반 루프),
  전투 RNG·FX tick(U1 임시로 `TurnHelper`).
- 결정: [[ADR-022-Battle-Session-Lifecycle]]. Task [[BS-001-Battle-Session]].

## Consumer Contract (U3 등반 루프가 쓰는 계약)

```csharp
var session = new BattleSession();
session.Configure(request);          // SceneTree 진입 전
session.Completed += OnBattleDone;    // Action<BattleResult>, 정확히 1회
host.AddChild(session);
session.Start();                      // 명시적 시작(자동 _Ready 시작 아님)
```

- `BattleRequest`(v0 축소): `PackedScene BattleScene`, `long Seed`, `NodePath PlayerPath`(전투 씬 루트 기준,
  예: `Articles/Ally/Character`). 새 전투 seed 생성 정책은 호출자 소유이며 세션은 받은 seed를 결과에 보존한다.
- `Completed`는 Godot signal이 아니라 C# `event Action<BattleResult>`다(scene-free 값 record 전달).
- `BattleResult`: `Outcome`/`EndReason`, `Seed`, `PlayerAlive`, `SurvivingAllies`/`SurvivingOpponents`,
  `IReadOnlyList<BattleUnitResult> Units`. `BattleUnitResult`: `SessionUnitId`(=`Faction:SpawnIndex`), `Faction`,
  `Survived`, `int? FinalHealth`, `Vector2I FinalTile`. 결과에 Godot 객체 참조/표시 이름/NodePath를 안정
  식별자로 두지 않는다. 보상·XP·WorldState 변경은 결과에 넣지 않는다.

## State Machine

```text
Created → Configure(request) → Configured → Start() → Running → (완료) → Completed
```

- `Configure`는 Created에서만, `Start`는 Configured에서만 유효하다. configure 전 start, 중복 configure,
  completed 뒤 start는 fail-closed(`GD.PushError` + no-op).
- `Start()`는 성공 시 `StartBattle` **직전에** Running으로 전환한다 — 첫 행동자의 `ApplyTurnStartEffects`가
  외부 코드로 유닛을 죽여도(예: 음수 `HealthRegen`) 그 사망이 완료로 이어지게 한다.

## Outcomes

| 상황 | Outcome | EndReason |
|---|---|---|
| 상대 생존자 0 | `Victory` | `OpponentsEliminated` |
| 지정 PC 사망(다른 Ally 생존 무관) | `Defeat` | `PlayerDefeated` |
| 동시 PC 사망 + 상대 전멸(mutual kill) | `Defeat` | `PlayerDefeated` |
| 잘못된 구성(씬/노드/PC/진영/instantiate) | `Aborted` | `InvalidSetup` |

- **mutual kill 우선순위**: PC 사망을 먼저 평가하는 Defeat 우선(OD5 확정). 완료 판정이 deferred라 같은 프레임의
  모든 사망이 기록된 뒤 판정하므로 detection 순서와 무관하게 결정적이다.
- `Retreat`/`RoundLimit`/`ManualRetreat`/`SceneRemoved`는 타입 자리만 있고 U1에서 미발생(후속).

## Start Validation (fail-closed → Aborted/InvalidSetup)

`TryBeginBattle` 순서: 단일 세션 가드(`IsInstanceValid(BattleFieldScene.BattleField)`) → `BattleScene` null/
`!CanInstantiate()` → instantiate 타입(`is BattleFieldScene`) → 필수 노드(`TurnHelper`/`ArticlesContainer`) →
`AutoStart=false` 후 AddChild → 초기 진영(Ally/Opponent 비공백) → PC 해석(`Articles["Ally"].Contains(player)` +
`ITurnAffectedArticle<ArticleBase>`, 부모 이름이 아니라 registry 등록으로 검증). 실패는 부분 생성 씬을 정리하고
crash/SCRIPT ERROR 없이 닫는다. 빈 PackedScene은 `CanInstantiate()` 선검사로 Godot 엔진 ERROR 없이 닫는다.

## Completion Order (event-identity + deferred)

사망은 `TurnHelper._PhysicsProcess → TurnPlay → Health.CurrentHealth setter → Dead() → OnDead` 콜스택에서
발생한다. `Health` setter가 `_currentHealth`를 0으로 clamp하기 **전에** `Dead()`를 emit하므로 OnDead 시점의
라이브 HP/`IsAlive`는 아직 사망 전 값이다. 세션은 이를 event-identity로 처리한다.

```text
OnDead(참가자)  [동기, 사망 콜스택]
  → 사망 유닛 기록: Survived=false, FinalHealth=0, FinalTile=현재(아직 valid)
  → 상대면 _aliveOpponents-- (세션 자체 bookkeeping, dict 제거 순서 비의존)
  → 완료 latch(_completionPending) 동기 설정 + CompleteBattleDeferred를 한 번 CallDeferred
CompleteBattleDeferred  [idle frame, 사망 콜스택 밖]
  → PC 사망 먼저 평가 → outcome 판정
  → 생존자 라이브 상태로 BattleResult 캡처(teardown 전)
  → TurnHelper.StopBattle → OnDead 구독 해제
  → RemoveChild(전투 씬) → BattleFieldScene._ExitTree가 정적 context 동기 null
  → QueueFree
  → Completed 1회 발행(subscriber별 try/catch 격리)
```

- teardown+event를 deferred로 미뤄 사망/물리 콜스택 안에서 tree를 수정하지 않는다. 완료 handler가 곧바로 다음
  세션을 시작해도(재진입) 정적 context가 이미 정리돼 stale singleton과 충돌하지 않는다.
- `BattleFieldScene._ExitTree()`는 `_battleFieldScene == this`일 때만 null로 만들어, 다른 세션이 이미 새
  인스턴스를 설정한 경우 덮어쓰지 않는다.
- 사망 유닛이 `queue_free`된 뒤에도 결과는 값 스냅샷이라 보존된다.

## Single Active Session

v0는 정적 `BattleFieldScene.BattleField` 제약으로 동시에 하나의 세션만 허용한다. 유효 컨텍스트가 있으면 두 번째
`Start`는 `Aborted/InvalidSetup`으로 거부한다. `run/main_scene`은 `workspace.tscn`이라 부팅 시 이 정적은 비어
있어 U1 test/host 경로가 깨끗하게 시작한다.

## Sequential Re-entry / Isolation

`battle_field.tscn`의 상태 리소스는 `resource_local_to_scene = true`이고 `SkillAmmoState`/`StatusController`는
per-instance 순수 C#이라, 세션마다 새 `PackedScene.Instantiate()`가 HP/ammo/상태를 완전 격리한다. 참가자 준비 +
전투 시작 ammo 충전은 신규 세션 경로에서 `TurnHelper` 밖(세션)이 소유한다. seed는 `TurnHelper.Configure`로
reflection 없이 주입한다.

## Verification

- `Assets/Script/Tests/bs001_step2_session_start_test.tscn`(79): 유효 시작(소유/Running/seed/세션 ammo),
  invalid matrix 9종(null/wrong-type/instantiate-fail/missing-TurnHelper/missing-Articles/PC miss/PC not
  Ally/forged-Ally/empty factions) deferred 1회, 단일 세션 가드, lifecycle fail-closed, Completed 예외 격리.
- `Assets/Script/Tests/bs001_step3_completion_test.tscn`(45): Victory(deferred/1회/결과 보존/정적 정리),
  Defeat(단일/생존 Ally), mutual kill Defeat 우선, 첫 턴 lethal 효과 완료, 2회 연속 재진입(seed/상태 격리).
- `Assets/Script/Tests/bs001_step1_lifecycle_test.tscn`(27): `TurnHelper` seam(AutoStart/configure/start/stop
  멱등) + 커서 결함 수정. [[Turn-System]] 참고.

## Known Gaps / Follow-ups

- `Retreat`/`RoundLimit`/수동 후퇴는 타입만 있고 미구현.
- `EncounterDefinition`/`EncounterModifier`/`PartySnapshot`으로 request 확장, `PlayerPath`→안정 `unit_id`.
- 명시적 Faction 타입/`BattleRoster`, `CombatContext`/`CombatRng` 이동, `BattleRecorder` 추출.
- RNG/FX tick은 U1 동안 `TurnHelper`에 남아 완전한 시뮬/프레젠테이션 분리는 아니다.

## Consumers

- 첫 production 소비자는 workspace 로비 진입 controller `LobbyBattleEntry`(BS-002/BS-003)다. World 창의
  `SubViewport` 아래에 세션을 장착해 로비 버튼→고정 `BattleRequest`→`Running`을 연결하고(BS-002), `Completed`의
  `BattleResult`를 소비해 결과를 최소 표시 + outgame/hub 복귀 + session cleanup을 하고 같은 버튼으로 재전투할 수
  있게 한다(BS-003). 완료 handler는 cleanup을 preset/presentation보다 먼저 수행하고(누수 방지), `LastResult`는
  scene-free 마지막 완료 결과만 보존한다. 사실은 [[Workspace-Window-System]] "Lobby Battle Entry"가 보존한다.
  보상·XP·주차·WorldState 정산과 U3 등반 루프는 후속이다.

## Related

- [[Turn-System]]
- [[Article-Status-System]]
- [[Workspace-Window-System]]
- [[ADR-022-Battle-Session-Lifecycle]]
- [[ADR-017-Deterministic-Combat-Resolution]]
- [[BS-001-Battle-Session]]
- [[BS-002-Lobby-to-Battle-Entry-Integration]]
- [[BS-003-Battle-Completion-Lobby-Return]]
