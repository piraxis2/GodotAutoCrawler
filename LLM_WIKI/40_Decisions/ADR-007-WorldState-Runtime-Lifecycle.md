---
id: ADR-007
type: decision
status: accepted
date: 2026-06-12
system: WorldState
---

# WorldState Runtime Lifecycle

## Context

DT-005는 타입 안전 `WorldStateStore`(schema validation, read/write/reset, SAVE/SESSION lifetime,
snapshot, atomic batch, provider facade)를 완성했다. 그러나 실제 런타임에는 연결되지 않았다.

현재 사실(실제 코드 확인):

- `project.godot` autoload에 `WorldStateStore`가 없다. `PlayerData` autoload는 있으나
  `Assets/Script/gds/player_data.gd`는 빈 `@tool extends Node`다.
- `Assets/Script/gds/world_state/world_state_store.tscn`의 내장 임시 Schema는 invalid다
  (key `testInt/testBoolean/testString`가 canonical 형식이 아니고 default 타입도 불일치).
- main scene은 `battle_field.tscn`이다.
- SaveGame/save slot/파일 포맷/새 게임 coordinator는 없다.

DT-006은 이 런타임 부팅과 수명주기 경계를 고정한다. 이 ADR은 그중 장기적으로 유지할 소유권과
초기화 정책 결정을 보존한다. 세부 API와 Step 구성은 [[DT-006-WorldState-Runtime-Integration]]에 둔다.

## Decision

### D1. Lifecycle 소유자 — 별도 coordinator

새 autoload `WorldStateRuntime`(coordinator)가 새 게임/load/SESSION orchestration을 담당한다.
`WorldStateStore`는 상태 보관·검증·snapshot 책임만 유지하고 세션 흐름을 모른다. 빈 `PlayerData`에
저장·세션·상태 책임을 몰아넣지 않는다(이름만으로 저장 책임을 추정하지 않음).

### D2. Autoload 이름과 접근 정책

- 상태 autoload 이름은 `WorldState`다(`/root/WorldState`). 클래스는 `class_name WorldStateStore`로
  유지한다. **autoload 이름은 class_name과 같으면 안 된다** — Godot이 전역 namespace를 공유해
  `WorldStateStore` autoload는 "Class 'WorldStateStore' hides an autoload singleton" 파싱 오류를 낸다
  (Step 2에서 실측 확인). 따라서 autoload는 `WorldState`로 명명한다.
- 세션 coordinator도 같은 제약을 받는다. autoload 이름은 `WorldStateRuntime`로 두되 클래스에는
  `class_name WorldStateRuntime`를 쓰지 않거나 다른 이름을 쓴다(Step 3에서 확정).
- 게임 시스템은 `/root/WorldState` autoload를 읽을 수 있다.
- Dialogue/조건 평가 계층은 autoload를 직접 조회하지 않고 read provider 주입 경계를 유지한다.
- 테스트는 `new()` + schema 주입을 계속 허용한다(autoload와 섞지 않음).

### D2a. Coordinator의 Store 획득 / autoload 순서 / 주입 계약

- `WorldStateRuntime`은 Store를 주입 가능한 단일 참조로 보관한다. 매 호출마다 `/root`를 문자열로
  조회하지 않는다.
- 획득 순서: (1) 명시적으로 주입된 Store(테스트)가 있으면 그것을, (2) 없으면 `_ready()`에서 `WorldState`
  autoload 싱글톤을 1회 해석해 보관한다.
- 테스트 계약: coordinator를 `new()` + `set_store(store)`로 주입한다. 주입된 경우 autoload를
  조회하지 않는다(autoload와 주입 Store가 섞이지 않음).
- **autoload 등록 순서**: `project.godot`에서 `WorldState`(Store)가 `WorldStateRuntime`(coordinator)보다
  **먼저** 등록돼야 한다. 그래야 coordinator `_ready()` 시점에 Store autoload가 이미 ready(부팅 default init 완료)다.
  기존 `DialogueManager` 등 다른 autoload와 이름이 충돌하지 않는다.

### D3. 초기화 실패 정책 — 명시적 실패

invalid/missing Schema면 Store는 not-ready를 명시적으로 보고하고 coordinator는 gameplay-ready로
전환하지 않는다. default Dictionary나 빈 Store로 조용히 대체하지 않는다. 손상된 상태를 정상 진행으로
오인해 새 게임처럼 덮어쓰는 일을 막는다.

### D4. SESSION reset 시점 — 새 게임 + SAVE load만

SESSION lifetime은 새 게임 시작과 SAVE snapshot load에서만 default로 시작한다. 일반 map/scene 교체,
대화 시작/종료에서는 자동 reset하지 않는다(맵 이동 시 대화 기억 유지). 더 짧은 conversation-local
상태는 World State가 아니라 별도 context로 둔다.

### D4a. Restore 실패 시 상태 보존 — transactional (pre-validation)

`restore_game()`은 `initialize()`(default reset) 후 `import_snapshot()` 하는 단순 순서를 쓰지 않는다.
그러면 snapshot이 whole-reject(malformed 구조/version 불일치)일 때 reset이 먼저 일어나 기존
SAVE/SESSION 값이 이미 소실된다.

정책(transactional):

- restore는 mutation 전에 snapshot envelope(최상위 구조 + `schema_version` 일치)를 **비변경**으로
  먼저 검증한다. 이를 위해 Store에 비변경 점검 API(예: `peek_snapshot_compatibility(snapshot) -> {ok, reason}`)를
  둔다(`_schema_version`/JSON-safe 규칙 재사용, 값/시그널 불변).
- envelope 검증 실패면: `initialize()`를 호출하지 않아 **기존 상태를 그대로 보존**하고, coordinator는
  실패를 보고하며 **session-ready로 전환하지 않는다**(gameplay 진입 금지).
- envelope 검증 통과면: `initialize()`(default) → `import_snapshot()` 순으로 commit한다. 이때
  개별 unknown/SESSION/type-mismatch 항목은 DT-005 replace-load 규칙대로 무시·report되며, 이는
  whole-reject가 아니라 부분-with-report 성공이다.
- 결과적으로 "복원 가능한 snapshot만 reset을 유발"하고, 손상/버전불일치 snapshot은 기존 상태를
  파괴하지 않는다. 손상 save를 새 게임처럼 덮어쓰는 사고를 막는다(Risk: invalid 실패의 gameplay-ready 오인).

### D5. Snapshot Adapter 소유권 — coordinator

SaveGame core는 WorldState를 몰라야 하므로 WorldState snapshot의 capture/restore/compatibility adapter는
coordinator(`WorldStateRuntime`)에 둔다. Runtime은 SaveGame을 참조하지 않고 WorldState Store 계약만 감싼다.

```text
capture_world_state() -> Dictionary                   # Store의 SAVE snapshot 반환(파일 미작성)
peek_world_state_compatibility(snapshot) -> Dictionary # Store의 비파괴 snapshot compatibility 점검
restore_world_state(snapshot) -> report                # lifecycle 경유 복원
```

`peek_world_state_compatibility()`는 Store의 `peek_snapshot_compatibility()`를 호출하는 얇은 public adapter다.
`WorldStateSaveSection.validate_save()`는 이 API를 사용해 restore 전에 snapshot envelope/schema 호환성을 확인한다.
이를 위해 SaveGame 쪽이 Store 내부 API를 직접 알 필요는 없다.

`capture_world_state()`가 파일 저장 가능성을 보장하지는 않는다. SaveGame adapter는 capture 전에 Store ready와
session ready를 확인해야 하며, 준비되지 않은 경우 실패 report를 반환하고 빈 payload를 저장하지 않는다.

실제 `FileAccess` 저장·slot·백업은 Store와 coordinator 밖의 별도 SaveGame Task로 둔다.

### D6. Bootstrap Schema — 최소 canonical key

제품 quest/actor 데이터 계약이 없으므로 대규모 가짜 상태를 만들지 않는다. 통합을 증명할 최소
canonical key(타입·SAVE/SESSION lifetime을 모두 포함)만 등록하고, 각 key의 제품 의미·소유자가
확인된 뒤 확장한다.

### 중복 초기화 / ready 의미 분리

- Store autoload `_ready()`는 부팅 시 export된 Schema로 1회 `initialize()`해 default ready 상태를 만든다.
- coordinator는 부팅 중 Store를 재초기화하지 않는다. `start_new_game()`/`restore_game()`만 명시적으로
  재초기화한다(중복 초기화 방지, Risk #1).
- "autoload ready(Store 준비)"와 "새 게임/load 완료(gameplay 가능)"는 서로 다른 사실로 구분해
  보고한다(Risk #2). load 실패 시 default 상태를 gameplay-ready로 오인하지 않는다.

## Alternatives

- **PlayerData 확장(C)**: 새 autoload는 없지만 빈 PlayerData에 저장·세션·상태 책임이 결합돼 향후 분리가
  어렵다. 거부.
- **Store가 직접 orchestration**: autoload 1개로 단순하나 상태 보관과 세션 정책이 한 클래스에 섞여
  테스트·확장이 나빠진다. 거부.
- **scene 교체마다 SESSION reset**: 단순하지만 맵 이동마다 대화/세션 기억이 사라진다. 거부.

## Consequences

### Positive

- 상태 보관(Store)과 세션 orchestration(coordinator)이 분리돼 책임이 명확하다.
- Dialogue는 provider 주입 경계를 유지해 `/root` 결합과 테스트 곤란을 피한다.
- 새 게임/load/SESSION 시점이 단일 소유자에 고정돼 코드·테스트로 검증 가능하다.
- 외부 SaveGame 시스템이 소비할 snapshot adapter 경계가 작게 유지된다.

### Negative

- autoload가 하나 늘어난다(`WorldStateRuntime`).
- "Store ready"와 "세션 준비"를 구분 보고해야 하므로 호출자가 두 사실을 다뤄야 한다.
- 실제 save file/slot은 여전히 후속 Task가 필요하다.

## Follow-ups

- SaveGame file/slot system (adapter 소비)
- schema migration / key alias ADR
- ConditionSet + ConditionEvaluator, State Read/Set Effect 노드

## Related

- [[DT-006-WorldState-Runtime-Integration]]
- [[DT-005-StateSchema-WorldStateStore]]
- [[ADR-006-Typed-World-State]]
- [[World-State-System]]
