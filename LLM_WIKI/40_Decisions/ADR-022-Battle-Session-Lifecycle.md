---
id: ADR-022
type: decision
status: accepted
system: Combat
created: 2026-07-13
updated: 2026-07-13
tags: [decision, combat, battle-session, lifecycle]
---

# BattleSession Owns Battle Runtime Lifecycle

## Status

Accepted. [[BS-001-Battle-Session]] Step 0 설계 리뷰(2026-07-13, [[BS-001-Battle-Session-Review]])에서 `Approved
after design fixes`로 승인. 리뷰 반영: (1) 완료 teardown+event는 사망/`Start` 콜스택 밖 deferred로 실행하고
완료 latch만 동기 설정, (2) 사망 유닛 결과/판정은 `OnDead` event identity를 진실로 취급(라이브 HP/`IsAlive`
재조회 금지 — `Health` setter가 clamp 전에 `Dead()`를 emit), (3) 상대 전멸 판정은 세션 자체 bookkeeping으로 하고
`ArticlesContainer` dict 제거 순서에 의존하지 않음. 신규 결정: 동시 PC 사망+상대 전멸(mutual kill) 우선순위는
**Defeat 우선(`Defeat/PlayerDefeated`)으로 사용자 확정(2026-07-13)** — PC 사망을 먼저 평가한다. 특수 콘텐츠의
다른 취급은 향후 `BattleRules` 오버라이드로 처리한다.

## Context

현재 `battle_field.tscn`은 SceneTree 진입과 동시에 `TurnHelper._Ready()`가 전투를 시작한다. `TurnHelper`는 참가자 수집, ammo 초기화, RNG, 턴 실행, 진영 공백 기반 종료 감지를 동시에 소유하지만 외부에 결과를 반환하지 않는다. `BattleFieldScene.BattleField` 정적 컨텍스트도 씬 제거 시 자동 정리되지 않는다.

데모 U1과 이후 DungeonRun은 명시적 전투 입력, 시작 전 설정, PC 사망/적 전멸 구분, 값 결과, 안전한 반복 실행을 요구한다.

## Decision

### 1. BattleSession is the lifecycle owner

`BattleSession : Node`가 전투 씬을 직접 인스턴스화하고 자식으로 소유한다. 입력 검증, 시작 준비, 승패 판정, 결과 캡처, 구독 해제, 씬 제거를 한 수명 주기에 묶는다.

게임 루프는 `BattleRequest`를 전달하고 `BattleResult`를 소비한다. Session 내부 전투 객체 참조는 결과 밖으로 유출하지 않는다.

### 2. Explicit start, single completion

신규 Session 경로는 `_Ready()` 자동 시작을 사용하지 않는다. `Configure` 후 `Start`를 명시적으로 호출하고, 결과는 정리 완료 후 C# event로 정확히 한 번 전달한다.

`TurnHelper`는 configure/start/stop 상태를 제공한다. 기존 직접 씬 실행 호환을 위해 `AutoStart=true` seam을 U1 동안 제한적으로 허용하되 신규 제품 경로에서는 사용하지 않는다.

### 3. PC-specific defeat belongs outside TurnHelper

- 지정 PC 사망 → `Defeat/PlayerDefeated`.
- 상대 생존자 0 → `Victory/OpponentsEliminated`.

`TurnHelper`는 faction, PC, 승패 의미를 판단하지 않는다. 외부 stop 요청에 따라 턴 진행을 멈춘다.

### 4. Start with a reduced request

U1 `BattleRequest`는 `PackedScene`, seed, 전투 씬 상대 `PlayerPath`만 요구한다. 장기적으로 encounter, modifiers, party snapshot, battle rules를 추가할 수 있게 request/result 값 객체 경계를 유지한다.

### 5. Result contains battle facts, not settlement

`BattleResult`는 outcome/reason, seed, 참가자 생존과 최종 HP/위치 같은 전투 사실만 담는다. 보상, XP, 주차, WorldState, 저장, UI 전환은 호출자 소유다.

### 6. Preserve current RNG and presentation coupling temporarily

U1에서는 ADR-017의 `TurnHelper` 소유 CombatRng와 `FxPlayer.Tick()` 결합을 유지한다. RNG/판정 context와 프레젠테이션 분리는 후속 ADR/Task가 담당한다.

### 7. Single active session under the current static context

`BattleFieldScene.BattleField` 정적 접근을 U1에서 제거하지 않는다. 대신 `_ExitTree()`에서 자신이 현재 인스턴스일 때만 null로 만들고, 이미 유효한 전투가 있으면 두 번째 Session 시작을 거부한다.

## Consequences

### Positive

- U3/DungeonRun이 전투 씬 내부 구현을 모르고 전투를 호출할 수 있다.
- PC 사망 규칙이 진영 전멸 판정과 분리된다.
- 결과가 scene-free 값이므로 정산, 로그, 테스트가 안전하게 소비한다.
- 반복 실행과 teardown이 제품 계약으로 고정된다.
- `TurnHelper`가 턴 스케줄러/실행기로 축소되는 방향이 생긴다.

### Negative

- U1 동안 직접 씬 실행용 `AutoStart`와 Session 명시 시작의 이중 경로가 존재한다.
- 정적 `BattleFieldScene.BattleField` 때문에 동시 세션을 지원하지 못한다.
- RNG와 FX가 `TurnHelper`에 남아 완전한 책임 분리는 아니다.
- `PlayerPath`와 문자열 faction은 임시 기술 부채다.

### Risks

- 사망 signal과 진영 공백이 같은 frame에 중복 완료를 유발할 수 있어 completed latch가 필수다.
- `queue_free()` 뒤에는 사망 유닛 상태를 잃을 수 있어 사전 기록이 필요하다.
- event 발생 전에 씬/정적 context를 정리하지 않으면 handler가 즉시 다음 Session을 시작할 때 stale context와 충돌한다.
- 현재 턴 유닛 제거 시 index reset 결함을 함께 고쳐야 한다.

## Alternatives Considered

### Keep game-over handling inside TurnHelper

기각. PC 사망, 진영 전멸, 한계 턴, 대회/방어전 규칙이 턴 스케줄러에 계속 누적된다.

### Make BattleSession a global autoload service

기각. Session은 전투 한 번의 명확한 Node 수명을 가져야 하며 전역 서비스로 두면 stale scene, 재진입, 테스트 격리가 어려워진다.

### Remove BattleFieldScene static context in U1

보류. 다수의 피해/스킬/이동/FX 코드와 회귀 테스트를 동시에 바꾸므로 Session 경계 작업을 과도하게 확장한다.

### Implement final Encounter/PartySnapshot API immediately

기각. 아직 해당 데이터 모델과 등반 수명 주기가 없고, 기존 전투 씬 반복 실행이라는 U1 완료 조건보다 일반화 비용이 크다.

## Follow-ups

- request를 Encounter/Modifier/PartySnapshot/BattleRules로 확장.
- 명시적 Faction/BattleRoster 도입.
- CombatContext와 BattleRecorder 추출.
- 정적 BattleField context 제거와 병렬 모의전 검토.
- 순수 TurnResolver + 이벤트 기반 presentation/replay.

## Related

- [[BS-001-Battle-Session]]
- [[ADR-017-Deterministic-Combat-Resolution]]
- [[ADR-021-Skill-Ammo-System]]
- [[Turn-System]]

