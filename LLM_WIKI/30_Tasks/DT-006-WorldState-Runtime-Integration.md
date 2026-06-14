---
id: DT-006
type: task
status: done
system: WorldState
created: 2026-06-12
updated: 2026-06-12
tags: [task, world-state, runtime, autoload, lifecycle]
---

# WorldState Runtime Integration

## Goal

DT-005에서 완성한 타입 안전 `WorldStateStore`를 실제 게임 런타임의 안정된 단일 상태 서비스로
연결한다.

이 Task가 끝나면 게임 시작 시 유효한 Schema로 Store가 준비되고, 새 게임과 snapshot load가
명시적인 순서로 실행되며, SESSION 상태의 초기화 시점이 코드와 테스트로 고정돼야 한다.

```text
game boot
  -> valid StateSchema compile
  -> WorldStateStore ready
  -> new game defaults 또는 SAVE snapshot import
  -> SESSION lifecycle 시작
  -> gameplay / Dialogue provider 주입
```

## Context

현재 사실:

- DT-005 Step 1~6은 완료됐다([[DT-005-WorldState-Review]]).
- `WorldStateStore`는 schema validation, read/write/reset, lifetime, snapshot, atomic batch,
  read/mutation provider facade를 제공한다.
- `project.godot`에는 `WorldStateStore` autoload가 등록돼 있지 않다.
- `Assets/Script/gds/world_state/world_state_store.tscn`의 내장 임시 Schema는 invalid다.
  key가 canonical 형식이 아니고 default가 선언 타입과 일치하지 않아 그대로 실행하면
  `schema is invalid; store not ready`가 발생한다.
- `PlayerData` autoload는 존재하지만 `Assets/Script/gds/player_data.gd`는 현재 빈 Node다.
- 별도의 SaveGame, save slot, 파일 포맷, 새 게임 coordinator는 아직 없다.
- Dialogue는 `DialogueManager.play(resource, read_state_provider)`로 provider를 주입받을 수 있으나,
  현재 State Read/Condition/Mutation Dialogue 노드는 없다.

따라서 이 Task는 기존 저장 시스템에 연결하는 작업이 아니라, World State의 런타임 부팅과
수명주기 경계를 먼저 고정하는 작업이다. 실제 save slot/file 시스템 전체를 임의로 발명하지 않는다.

## User Outcome

- 프로젝트를 실행하면 World State가 invalid 오류 없이 ready다.
- 게임 코드는 `/root/WorldState`(class `WorldStateStore`)에서 등록 상태를 읽고 변경할 수 있다.
- 새 게임은 모든 상태를 Schema default로 시작한다.
- snapshot load는 Store 초기화 후 SAVE 값을 복원하고 SESSION을 default로 시작한다.
- SESSION reset의 호출 시점과 책임자가 하나로 정해져 있다.
- Dialogue를 시작할 때 같은 Store를 read provider로 전달할 수 있다.
- invalid/missing Schema와 잘못된 snapshot은 조용히 진행되지 않고 명시적으로 실패한다.

## Scope

### Included

- 실제 사용 가능한 게임용 `StateSchema.tres`
- 유효 Schema를 할당한 `WorldStateStore` scene
- `WorldStateStore` autoload 등록
- boot readiness와 중복 초기화 정책
- 새 게임 defaults 진입점
- snapshot load 순서와 report 전달 경계
- SESSION reset 시점 및 반복 호출 정책
- World State snapshot을 외부 저장 계층이 소비할 최소 adapter 계약
- runtime integration과 DT-005 회귀 테스트
- 관련 System/User Guide/Current-State 갱신

### Out of Scope

- save slot 선택 UI
- 여러 슬롯의 파일명·디렉터리·백업·클라우드 동기화
- 암호화, 압축, 체크섬
- schema migration과 key alias
- State Read/Set/Add Dialogue 노드
- ConditionSet/ConditionEvaluator
- Response Selector와 조건부 Choice
- State Inspector/History
- full int64 snapshot
- 기존 `PlayerData`를 근거 없이 저장 시스템으로 확장하는 작업

## Design Constraints

- `WorldStateStore`는 계속 파일 경로와 슬롯을 모른다.
- 게임용 Schema는 `.tres`로 관리하고 Store scene이 참조한다.
- invalid Schema에서는 부분 동작하지 않는다.
- autoload 등록 후 gameplay가 별도로 `initialize()`를 반복 호출하지 않는다.
- 새 게임과 load는 서로 다른 명시적 진입점이어야 한다.
- load 순서는 `peek compatibility(비변경) -> (성공 시) initialize/defaults -> import SAVE snapshot
  -> SESSION default 유지`다. envelope 점검 실패 시 initialize 없이 기존 상태를 보존한다(ADR-007 D4a).
- snapshot version 불일치와 malformed snapshot은 기존 값을 부분 적용하지 않으며, reset도 유발하지 않는다.
- Dialogue runtime은 Store를 직접 `/root`에서 찾지 않고 provider 주입 경계를 유지한다.
- `PlayerData`는 현재 비어 있으므로, 책임을 부여하려면 설계 리뷰에서 명시적으로 승인한다.
- 한 번에 한 Step만 구현하고 각 Step 리뷰의 P0/P1을 해결한 뒤 다음 Step으로 진행한다.

## Open Decisions Before Implementation — 확정됨 (Step 0)

> 상태: 모든 항목이 Step 0 설계 리뷰에서 확정됐다(판정 Approved after design fixes → 보충 반영).
> 장기 결정은 [[ADR-007-WorldState-Runtime-Lifecycle]]에 기록했다. 아래는 원래 후보와 최종 선택의
> 근거를 보존하기 위한 기록이며, 확정 결론은 위 "Step 0 결과"와 ADR-007이 기준이다.

### D1. Runtime Lifecycle 소유자

선택지:

- A. `WorldStateStore` 자체가 new/load orchestration까지 담당
- B. 별도 `WorldStateRuntime` 또는 GameSession coordinator가 Store를 호출
- C. 빈 `PlayerData`를 확장해 담당

권장: **B**. Store의 상태 보관 책임과 게임 세션 orchestration을 분리한다. `PlayerData`는 이름만으로
저장 책임을 추정하지 않는다.

### D2. Autoload 이름과 접근 정책

권장 이름은 `WorldStateStore`다. 게임 시스템은 autoload를 사용할 수 있지만 Dialogue/조건 계층은
provider 주입을 유지한다. 테스트에서는 계속 `new()+schema 주입`을 허용한다.

### D3. 초기화 실패 정책

권장: invalid/missing Schema면 ready false 상태를 명시적으로 보고하고 gameplay 진입을 중단한다.
default Dictionary나 빈 Store로 조용히 대체하지 않는다.

### D4. SESSION Reset 시점

정의가 필요한 사건:

- 애플리케이션 부팅
- 새 게임 시작
- SAVE snapshot load
- 메인 메뉴 복귀
- 전투/맵 scene 교체
- 대화 시작/종료

권장: SESSION은 **새 게임과 SAVE load에서 default로 시작**하고, 일반 scene 교체나 대화 종료에서는
자동 reset하지 않는다. 더 짧은 conversation-local 상태는 World State가 아니라 별도 context로 둔다.

### D5. Snapshot Adapter 소유권

현재 SaveGame 시스템이 없으므로 이번 Task에서는 다음 최소 계약까지만 고정한다.

```text
capture_world_state() -> Dictionary
restore_world_state(snapshot: Dictionary) -> report
```

실제 파일/slot 구현은 별도 Task로 둔다. 이 adapter를 `PlayerData`에 둘지 새 coordinator에 둘지는
D1과 함께 확정한다.

### D6. Bootstrap Schema 내용

프로젝트에 실제 quest/actor 데이터 계약이 아직 없으므로 대규모 가짜 상태를 넣지 않는다.
권장: 통합을 증명할 최소 canonical key만 등록하고 각 key의 제품 의미·소유자가 확인된 뒤 확장한다.

## Runtime Contract (Step 0 확정 방향)

소유자는 `WorldStateRuntime` coordinator로 확정됐다(ADR-007 D1). 아래 API는 책임·테스트 기준이며
세부 시그니처는 Step 3 구현에서 고정한다.

```gdscript
# Session/lifecycle coordinator 예시
signal world_state_ready(mode: StringName, report: Dictionary)
signal world_state_failed(mode: StringName, report: Dictionary)

func start_new_game() -> Dictionary
func restore_game(world_state_snapshot: Dictionary) -> Dictionary
func capture_world_state() -> Dictionary
func is_world_state_ready() -> bool
```

행동 계약:

- `start_new_game()`은 Store를 유효 Schema default로 재초기화한다.
- `restore_game()`은 먼저 snapshot envelope(구조+version)를 비변경으로 점검(peek)하고, 통과 시에만
  Store를 default로 재초기화한 뒤 snapshot을 import한다(ADR-007 D4a, transactional).
- envelope 점검 실패(malformed/version mismatch)면 initialize를 호출하지 않아 기존 상태를 보존하고,
  ready gameplay로 전환하지 않는다.
- `capture_world_state()`은 Store의 SAVE snapshot을 반환할 뿐 파일을 쓰지 않는다.
- 성공/실패 report는 호출자와 signal subscriber가 서로 변조하지 않도록 분리한다.
- lifecycle 실행 중 재진입 또는 중복 요청 정책을 명시한다.

## Steps

## Step 0: Design Review and Lifecycle Decision

목표:

- 실제 코드 구조와 이 Task를 대조하고 D1~D6을 확정한다.

작업 범위:

- `project.godot`, `PlayerData`, 메인 scene, DT-005 Store API 검토
- lifecycle owner, autoload 이름, 실패 정책, SESSION reset, adapter 책임 결정
- 장기 결정이면 ADR-007 작성 또는 이 Task에 승인된 결론 기록

제외 범위:

- 제품 코드와 `.tscn`/`.tres` 수정

완료 조건:

- 설계 리뷰 판정이 `Approved` 또는 `Approved after design fixes`
- D1~D6에 미결정 항목이 없음
- Step 1~5의 API와 소유 경계가 구현 가능한 수준으로 확정됨

검증 방법:

- [[Design-Review-Prompt]]로 Task/ADR/실제 코드 대조

선행 조건: 없음

### Step 0 결과 — 설계 리뷰 완료 (2026-06-12, 판정: Approved)

실제 코드 대조(확인):
- `project.godot` autoload에 `WorldStateStore` 없음. `PlayerData`는 빈 `@tool extends Node`.
- `world_state_store.tscn` 내장 임시 Schema는 invalid(non-canonical key `testInt/testBoolean/testString`,
  default 타입 불일치) → 그대로 실행 시 `schema is invalid; store not ready`.
- main scene은 `Assets/Scenes/Map/battle_field.tscn`. DT-005 Store API는 Task 기술과 일치.

확정 결정(→ [[ADR-007-WorldState-Runtime-Lifecycle]]):
- **D1**: 새 `WorldStateRuntime` coordinator autoload가 new/load/SESSION orchestration 담당.
  `WorldStateStore`는 상태 보관·검증·snapshot만 유지(분리).
- **D2**: autoload — 상태 `WorldState`(class `WorldStateStore`, 이름 충돌 회피), 세션 `WorldStateRuntime`.
  게임 시스템은 autoload 사용 가능,
  Dialogue/조건 계층은 provider 주입 유지, 테스트는 `new()`+주입.
- **D3**: invalid/missing Schema는 명시적 not-ready, gameplay 진입 중단. 조용한 default 대체 금지.
- **D4**: SESSION reset은 새 게임 + SAVE load에서만. scene 교체/대화 종료 자동 reset 없음.
- **D5**: snapshot adapter(`capture_world_state`/`restore_world_state`)는 coordinator 소유.
  실제 file/slot IO는 후속 SaveGame Task.
- **D6**: 최소 canonical bootstrap key만 등록(타입·SAVE/SESSION 포함), 제품 key 확정 후 확장.
- 중복 초기화/ready 의미 분리: Store `_ready()`가 부팅 시 1회 default init, coordinator는 부팅 중
  재초기화하지 않고 `start_new_game()`/`restore_game()`에서만 재초기화. "Store ready"와 "세션 완료"를
  구분 보고.

설계 리뷰 보충(2차, "Approved after design fixes" 반영):
- **D2a** Store 획득/주입/autoload 순서: coordinator는 주입된 Store 우선·없으면 autoload 1회 해석,
  `new()+set_store()` 주입 지원, `WorldStateStore`→`WorldStateRuntime` 등록 순서 강제(→ ADR-007 D2a).
- **D4a** transactional restore: restore는 mutation 전 snapshot envelope(구조+version)를 비변경 점검,
  통과 시에만 reset+import. 실패면 기존 상태 보존·session-ready 미전환(→ ADR-007 D4a).
- **D6** bootstrap schema: Step 1에 key 6개 표로 확정(key_count=6).

미결정 항목 없음. Step 1~5의 API와 소유 경계는 구현 가능한 수준으로 확정됨.
선행 조건 충족: Step 1(D6 확정) 진행 가능.

## Step 1: Bootstrap Schema and Store Scene

목표:

- 유효한 게임용 Schema와 실행 가능한 Store scene을 만든다.

작업 범위:

- 별도 `StateSchema.tres` 생성
- canonical key, strict default type, lifetime, writable 설정
- `world_state_store.tscn`에서 invalid 임시 subresource 제거
- Store scene이 새 Schema를 참조하도록 구성
- Schema validation 및 `.tres` 저장/재로드 검증

확정된 Bootstrap Schema (D6, key_count = 6):

| key | value_type | default | lifetime | writable |
| --- | --- | --- | --- | --- |
| `quest.main.stage` | INT | `0` | SAVE | true |
| `actor.example.affinity` | INT | `0` | SAVE | true |
| `player.health` | FLOAT | `100.0` | SAVE | true |
| `player.display_name` | STRING | `""` | SAVE | true |
| `world.build.channel` | STRING_NAME | `&"dev"` | SAVE | false (read-only) |
| `session.intro.seen` | BOOL | `false` | SESSION | true |

- 5개 value_type(INT/FLOAT/STRING/STRING_NAME/BOOL), SAVE/SESSION 두 lifetime, writable/read-only를
  모두 포함하는 최소·대표 집합이다. 제품 의미 미확정 placeholder이며 실제 quest/actor key는 소유자
  확인 후 확장한다(D6). `schema.validate()`는 valid, `key_count == 6`이어야 한다.

제외 범위:

- autoload 등록
- lifecycle coordinator
- 파일 저장
- 실제 대화 조건 노드

완료 조건:

- Store scene 직접 실행/인스턴스화 시 `is_store_ready()==true`
- `schema.validate()`가 valid이며 key_count가 예상과 일치
- editor 저장 후 cache 무시 재로드에서도 key/type/default/lifetime/writable 보존
- `schema is invalid` 오류가 발생하지 않음

검증 방법:

- Step 1 전용 headless test
- Store scene load/instantiate
- `.tres` 저장·재로드 왕복
- Godot headless editor load

선행 조건: Step 0 승인, D6 확정

### Step 1 결과 — 구현 완료 (2026-06-12, 코드 리뷰 대기)

**변경 파일**
- `Assets/Script/gds/world_state/world_state_schema.tres` (신규): 확정 6-key bootstrap Schema.
- `Assets/Script/gds/world_state/world_state_store.tscn`: invalid 임시 embedded schema 제거,
  외부 `world_state_schema.tres`를 `schema`로 참조하도록 교체(node/uid 보존).
- `Assets/Script/gds/world_state/tests/dt006_step1_bootstrap_test.gd` (+ `.tscn`): Step 1 검증.

**구현 내용**
- 일회성 생성기로 Godot-valid `.tres`를 작성한 뒤 생성기는 제거(산출물 .tres만 커밋). 6 key는
  `quest.main.stage`/`actor.example.affinity`(INT,SAVE), `player.health`(FLOAT,SAVE),
  `player.display_name`(STRING,SAVE), `world.build.channel`(STRING_NAME,SAVE,read-only),
  `session.intro.seen`(BOOL,SESSION). schema_version=1.
- Store scene은 외부 .tres를 참조하므로 `is_store_ready()==true`로 부팅된다(invalid 오류 제거).

**검증 (Godot 4.6.3 mono headless)**
- `dt006_step1_bootstrap_test` → `[DT-006 Step1] ALL PASS`, exit 0:
  - A: `.tres` valid, key_count 6, 6 key 전부 type/default/typeof/lifetime/writable가 계약과 일치.
  - B: Store scene instantiate → ready, default 읽기, read-only `world.build.channel` gameplay set 거부.
  - C: `.tres` 저장 → cache 무시 재로드 왕복에서 전 필드 보존.
- DT-005 Step 1~6 회귀 ALL PASS, headless editor load 성공.

**완료 조건 대응**
- Store scene instantiate 시 `is_store_ready()==true` ✓, `schema.validate()` valid·key_count 6 ✓,
  재로드 보존 ✓, `schema is invalid` 오류 없음 ✓.

**남은 위험 / 이월**
- autoload 등록은 Step 2, lifecycle coordinator는 Step 3, snapshot adapter는 Step 4.
- bootstrap key는 placeholder(제품 의미 미확정) — D6대로 소유자 확인 후 확장.
- 자기 승인하지 않는다.

## Step 2: Autoload and Boot Readiness

목표:

- 게임 부팅 시 단 하나의 ready Store가 존재하도록 한다.

작업 범위:

- `project.godot`에 Store scene autoload 등록
- autoload 이름과 접근 경로 고정
- `_ready()` 자동 초기화와 외부 lifecycle 호출의 중복 책임 정리
- 초기화 실패를 gameplay 진입 전에 관찰할 readiness API/signal 연결
- 테스트에서 autoload와 주입 Store가 섞이지 않도록 경계 유지

제외 범위:

- 새 게임/load orchestration 구현
- snapshot 파일 IO
- Dialogue가 `/root`를 직접 조회하도록 변경

완료 조건:

- 프로젝트 부팅 직후 `/root/<approved name>`가 하나만 존재
- Store가 ready이며 bootstrap defaults를 읽을 수 있음
- missing/invalid Schema 대역에서 명시적 실패가 관찰됨
- scene 반복 진입과 교체로 Store가 중복 생성·재초기화되지 않음
- 기존 `DialogueManager`와 autoload 이름 충돌 없음

검증 방법:

- autoload integration headless test
- 메인 scene 반복 load/교체
- invalid Schema fixture 실패 테스트
- DT-005 Step 1~6 회귀

선행 조건: Step 1 완료, D2/D3 확정

### Step 2 결과 — 구현 완료 (2026-06-12, 코드 리뷰 대기)

**변경 파일**
- `project.godot`: `[autoload]`에 `WorldState="*res://Assets/Script/gds/world_state/world_state_store.tscn"` 추가.
- `Assets/Script/gds/world_state/tests/dt006_step2_autoload_test.gd` (+ `.tscn`): Step 2 검증.

**설계 수정(구현 중 발견) — autoload 이름**
- D2의 권장 autoload 이름 `WorldStateStore`는 `class_name WorldStateStore`와 전역 namespace가 충돌해
  "Class 'WorldStateStore' hides an autoload singleton" 파싱 오류를 낸다(실측 확인). → autoload 이름을
  **`WorldState`**(`/root/WorldState`)로 변경. 클래스는 `WorldStateStore` 유지. ADR-007 D2 갱신.

**구현 내용**
- Store scene을 `WorldState` autoload로 등록. 부팅 시 `_ready()`가 외부 `world_state_schema.tres`로
  1회 default 초기화 → ready. 외부 코드는 부팅 중 `initialize()`를 재호출하지 않는다(중복 init 방지).
  ready 관찰은 `is_store_ready()` API로 한다(reactive ready/failed signal은 Step 3 coordinator).

**검증 (Godot 4.6.3 mono headless)**
- `dt006_step2_autoload_test` → `[DT-006 Step2] ALL PASS`, exit 0:
  - A: `/root/WorldState` 존재·`WorldStateStore` 타입·ready·bootstrap default(stage 0, health 100.0,
    channel StringName) 읽기.
  - B: root 직속 `WorldState` 정확히 1개, `DialogueManager`와 공존·구분, root에 parented(이름 충돌 없음).
  - C: invalid Schema(주입 인스턴스, autoload와 분리)는 not-ready로 명시적 관찰, autoload는 영향 없음.
  - D: transient scene churn 후에도 같은 인스턴스가 값·ready 유지(중복 생성·재초기화 없음).
- dt004 5종·dt005 Step 1~6·dt006 Step 1 회귀 ALL PASS, headless editor load 성공.

**완료 조건 대응**
- 부팅 직후 `/root/WorldState` 1개 ✓, ready + default 읽기 ✓, invalid Schema 명시적 실패 ✓,
  transient churn에 중복·재초기화 없음 ✓, 기존 autoload 이름 충돌 없음 ✓.

**남은 위험 / 이월**
- 실제 main scene boot/re-entry와 change_scene 회귀는 Step 5 end-to-end에서 검증한다(여기서는 autoload
  semantics + transient churn으로 확인).
- new/load orchestration은 Step 3, snapshot adapter는 Step 4.
- 자기 승인하지 않는다.

## Step 3: New Game and Session Lifecycle

목표:

- 새 게임 및 게임 load 전후의 World State 초기화 순서를 단일 진입점으로 고정한다.

작업 범위:

- 승인된 lifecycle owner(`WorldStateRuntime` coordinator) 구현
- Store 획득/주입(ADR-007 D2a): 주입된 Store 우선, 없으면 `_ready()`에서 `/root/WorldState` autoload
  1회 해석. coordinator `new()` + `set_store(store)` 주입 지원. autoload 등록 순서는
  `WorldState` → `WorldStateRuntime`.
- `start_new_game()` 또는 동등 API
- `restore_game(snapshot)` orchestration — **transactional**(ADR-007 D4a): mutation 전에 snapshot
  envelope(구조+`schema_version`)를 비변경으로 점검(Store `peek_snapshot_compatibility` 등)하고,
  통과 시에만 `initialize()`(default) → `import_snapshot()`. envelope 실패면 기존 상태 보존, 실패 보고,
  session-ready 미전환.
- SESSION reset 정책 구현(새 게임 + SAVE load에서만)
- 중복 호출, 재진입, 실패 후 재시도 정책
- 성공/실패 report 또는 signal("Store ready" ≠ "session-ready"를 구분 보고)

제외 범위:

- save slot/file 접근
- 메뉴 UI
- schema migration
- scene 교체마다 자동 SESSION reset

완료 조건:

- 새 게임은 SAVE/SESSION 모두 default
- load는 먼저 default 계약으로 재초기화 후 SAVE snapshot 적용
- load 직전의 SESSION 값은 유지되지 않고 default로 시작
- malformed/version mismatch snapshot에서는 gameplay-ready 성공을 보고하지 않음
- malformed/version mismatch restore는 `initialize()`(default reset)를 유발하지 않고 **기존 상태를
  보존**한다(envelope pre-validation 통과 시에만 reset+import)
- 실패 후 새 게임 또는 정상 snapshot으로 복구 가능
- 일반 map/scene 교체는 World State 값을 보존

검증 방법:

- new game 반복 실행
- mutation 후 new game default 복원
- SAVE/SESSION mutation 후 snapshot load
- malformed/version mismatch/unknown key report
- 재진입 및 중복 요청
- scene 교체 회귀

선행 조건: Step 2 완료, D1/D3/D4 확정

### Step 3 결과 — 구현 완료 (2026-06-12, 코드 리뷰 대기)

**변경 파일**
- `Assets/Script/gds/world_state/world_state_runtime.gd` (신규): coordinator. autoload `WorldStateRuntime`
  (class_name 없음 — 충돌 회피).
- `Assets/Script/gds/world_state/world_state_store.gd`: `peek_snapshot_compatibility(snapshot) -> {ok, reason}`
  비변경 envelope 점검 추가(D4a transactional restore용).
- `project.godot`: `WorldStateRuntime` autoload 등록(`WorldState` 다음 순서).
- `Assets/Script/gds/world_state/tests/dt006_step3_lifecycle_test.gd` (+ `.tscn`): Step 3 검증.

**구현 내용**
- `WorldStateRuntime` coordinator: Store 주입 우선·없으면 `/root/WorldState` 1회 해석(`set_store`/`get_store`),
  `is_store_ready()`(부팅) vs `is_session_ready()`(세션 완료) 구분.
- `start_new_game()`: Store를 default 재초기화(SAVE+SESSION default), 성공 시 session-ready·`world_state_ready`.
- `restore_game(snapshot)`: **transactional** — `peek_snapshot_compatibility`로 envelope 비변경 점검,
  통과 시에만 `initialize()`→`import_snapshot()`. 실패 시 reset 없이 기존 상태/세션 보존·`world_state_failed`.
  SAVE 복원·SESSION default(initialize가 default, import는 SAVE만).
- `capture_world_state()`: SAVE-only snapshot 반환(파일 미작성).
- `_busy` 재진입 가드, report는 signal/반환에 deep copy.

**검증 (Godot 4.6.3 mono headless)**
- `dt006_step3_lifecycle_test` → `[DT-006 Step3] ALL PASS`, exit 0:
  - A: start_new_game → SAVE/SESSION 전부 default + session-ready + `world_state_ready(new_game)`.
  - B: capture(SAVE-only) + restore round-trip, SAVE 복원·SESSION default, `world_state_ready(load)`.
  - C: malformed restore → 기존 상태/세션 보존(reset 없음), session-ready 유지, `world_state_failed`.
  - D: version mismatch restore → 보존.
  - E: import value_changed 중 재진입 lifecycle 호출 → `busy` 거부.
  - F: autoload `/root/WorldStateRuntime` 존재·`/root/WorldState` 연결·ready.
  - G: import 중 `set_store(other/null)` 거부 → Store 불변·restore 정상 적용·session-ready(리뷰 P1).
  - H: idle Store 교체 → session-ready 해제, null 주입은 런타임 오류 없이 not-ready 보고(리뷰 P1).
- dt004(spot)·dt005 Step 1~6·dt006 Step 1~2 회귀 ALL PASS, headless editor load 성공.

**Step 3 코드 리뷰 수정 (2026-06-12)**
- [P1] `set_store()`가 lifecycle transaction을 우회(callback에서 교체/null 주입 시 import는 기존
  Store에, ready/report는 새 Store 기준 → 불일치; null 시 런타임 오류; 평상시 교체 시 stale
  session-ready) → (1) `_busy` 중 `set_store()` 거부, (2) 실제 교체 시 `_session_ready=false`,
  (3) `start_new_game`/`restore_game`이 트랜잭션 동안 Store 참조를 지역 변수로 고정. 회귀 테스트 G/H 추가.
- 수정 후 재검증: Step 3 A~H 통과(exit 0), dt005 Step 1~6·dt006 Step 1~2 회귀 통과, editor load 성공.

**완료 조건 대응**
- 새 게임 default ✓, load는 (envelope 통과 시) default 재초기화 후 SAVE 적용 ✓, load 직전 SESSION 미유지·default ✓,
  malformed/version mismatch는 gameplay-ready 미보고 ✓·**기존 상태 보존**(reset 없음) ✓, 실패 후 복구 가능 ✓.

**남은 위험 / 이월**
- `restore_world_state(snapshot)` 외부 adapter 진입점은 Step 4에서 정리(현재 capture만 노출).
- 실제 main scene change_scene 회귀는 Step 5.
- 자기 승인하지 않는다.

## Step 4: Snapshot Adapter Boundary

목표:

- 외부 SaveGame 시스템이 World State를 저장·복원할 최소 adapter를 제공한다.

작업 범위:

- `capture_world_state()`에서 SAVE snapshot 반환
- `restore_world_state(snapshot)`에서 Step 3 lifecycle 경유
- JSON stringify/parse 가능한 데이터 계약 검증
- report 전달과 실패 전파
- adapter의 소유 클래스와 public API 문서화

제외 범위:

- 실제 `FileAccess` 저장
- slot 목록, autosave, backup
- PlayerData의 다른 게임 데이터 모델
- migration

완료 조건:

- capture 결과에 SAVE만 있고 SESSION은 없음
- JSON 왕복 후 restore가 타입과 값을 보존
- invalid data가 성공으로 보고되지 않음
- Store가 파일 경로나 slot을 알지 않음
- adapter가 Store 내부 `_values`/`_contract`에 접근하지 않고 public snapshot API만 사용

검증 방법:

- memory/JSON round-trip
- snapshot report 전달
- SESSION 비영속
- Store public boundary 정적 검토

선행 조건: Step 3 완료, D5 확정

### Step 4 결과 — 구현 완료 (2026-06-12, 코드 리뷰 대기)

**변경 파일**
- `Assets/Script/gds/world_state/world_state_runtime.gd`: `restore_world_state(snapshot)` adapter 진입점 추가
  (Step 3 `restore_game` transactional lifecycle 경유), adapter 경계 문서화.
- `Assets/Script/gds/world_state/tests/dt006_step4_adapter_test.gd` (+ `.tscn`): Step 4 검증.

**구현 내용**
- 외부 SaveGame adapter 쌍: `capture_world_state() -> Dictionary`(SAVE-only, 파일 미작성),
  `restore_world_state(snapshot) -> report`(restore_game 경유). coordinator는 Store의 public
  snapshot API(`export_snapshot`/`peek_snapshot_compatibility`/`import_snapshot`)만 호출하고
  `_values`/`_contract`에 접근하지 않는다. 파일 경로·슬롯·직렬화는 외부 책임.

**검증 (Godot 4.6.3 mono headless)**
- `dt006_step4_adapter_test` → `[DT-006 Step4] ALL PASS`, exit 0:
  - A: capture SAVE-only·JSON 호환(왕복 가능).
  - B: `restore_world_state` JSON stringify/parse 왕복 후 INT/FLOAT/STRING/STRING_NAME 타입·값 보존,
    SESSION default.
  - C: malformed/version mismatch는 ok=false + 기존 상태 보존.
  - D: not-ready capture는 빈 Dictionary(안전).
- dt006 Step 1~3·dt005 Step 1~6 회귀 ALL PASS, headless editor load 성공.

**완료 조건 대응**
- capture SAVE-only ✓, JSON 왕복 후 타입·값 보존 ✓, invalid 성공 미보고 ✓, Store는 파일/slot 미인지 ✓,
  adapter는 public snapshot API만 사용(내부 미접근) ✓.

**남은 위험 / 이월**
- 실제 file/slot 직렬화·autosave·backup은 별도 SaveGame Task.
- end-to-end(부팅→new→capture→restore→scene 교체→Dialogue 주입) 통합·완료 판정은 Step 5.
- 자기 승인하지 않는다.

## Step 5: Runtime Integration Regression and Review

목표:

- 실제 autoload Store의 boot/new/load/session/provider 흐름을 한 시나리오로 검증하고 완료 판정한다.

작업 범위:

- 프로젝트 부팅 -> Store ready
- new game -> mutation -> capture -> mutation -> restore
- SAVE 복원 및 SESSION default 확인
- scene 교체 값 보존
- 실제 Store를 `DialogueManager.play(dialogue, store)`에 주입
- DT-005/DialogueTool 기존 회귀
- 리뷰 문서와 Wiki 현재 사실 갱신

제외 범위:

- State 조건/Effect Dialogue 노드
- 실제 save file/slot UI

완료 조건:

- Step 1~5 테스트 통과
- DT-005 Step 1~6 전체 회귀 통과
- DialogueTool 관련 회귀 통과
- Godot headless editor load 성공
- P0/P1 없음
- accepted debt와 후속 save-file Task가 Review 문서에 명시됨

검증 방법:

- end-to-end headless integration scene
- 메인 scene boot/re-entry
- 전체 회귀 매트릭스
- 별도 코드 리뷰

선행 조건: Step 1~4 완료

### Step 5 결과 — end-to-end 통합·완료 판정 (2026-06-12)

**변경 파일**
- `Assets/Script/gds/world_state/tests/dt006_step5_integration_test.gd` (+ `.tscn`): end-to-end 통합.
- `LLM_WIKI/50_Reviews/DT-006-WorldState-Runtime-Review.md`: 통합 리뷰·판정.

**통합 시나리오 검증**
- `dt006_step5_integration_test` → `[DT-006 Step5] ALL PASS`, exit 0. 실제 autoload(`/root/WorldState`,
  `/root/WorldStateRuntime`)로: 부팅 ready(A) → start_new_game default+session-ready(B) →
  mutation→capture(SAVE-only)(C) → mutation→restore_world_state(SAVE 복원·SESSION default)(D) →
  transient scene churn 후 값 보존·동일 인스턴스(E) → 실제 Store를 `DialogueManager.play(dialogue, store)`에
  주입해 player read seam이 Store 값 라우팅(F).

**전체 회귀**
- dt006 Step 1~5, dt005 Step 1~6, DialogueTool dt004 5종(integration 포함) 전부 ALL PASS,
  headless editor load 성공.

**판정**
- [[DT-006-WorldState-Runtime-Review]]: **완료**. P0/P1 없음. 후속 SaveGame file/slot Task와
  accepted debt 명시.

## Verification Matrix

| 영역 | 정상 경로 | 실패·회귀 경로 |
| --- | --- | --- |
| Schema | valid `.tres`, 저장/재로드 | invalid key/default, missing schema |
| Boot | autoload 1개, ready | 초기화 실패, 이름 충돌, 중복 초기화 |
| New Game | 모든 default | 반복 호출, 기존 값 잔존 |
| Load | SAVE 복원, SESSION default | malformed, version mismatch, 실패 후 복구 |
| Scene Lifecycle | scene 교체 후 값 유지 | 중복 Store, 의도치 않은 SESSION reset |
| Snapshot Adapter | SAVE-only JSON 왕복 | SESSION 유출, 파일 책임 Store 침투 |
| Dialogue | provider 주입 후 read | Dialogue의 `/root` 직접 조회 회귀 |
| Compatibility | DT-005/DT-004 통과 | 기존 signal/reentrancy 계약 회귀 |

## Risks

- Store `_ready()` 자동 초기화와 lifecycle의 명시적 재초기화가 겹치면 부팅 중 값이 두 번 초기화될 수 있다.
- autoload가 ready라는 사실과 “새 게임/load가 완료돼 gameplay 가능”이라는 사실을 혼동할 수 있다.
- scene 진입을 SESSION 경계로 잘못 사용하면 맵 이동마다 대화 기억이 사라진다.
- 빈 `PlayerData`에 저장·세션·World State 책임을 모두 몰아넣으면 향후 분리가 어려워진다.
- invalid snapshot 실패 후 default 상태를 gameplay-ready로 오인하면 손상된 save를 새 게임처럼 덮어쓸 수 있다.
- 실제 제품 key가 확정되지 않은 상태에서 대량 bootstrap key를 만들면 이름과 migration 부채가 생긴다.

## Completion Criteria

- 유효한 게임용 Schema와 Store scene이 존재한다.
- Store가 autoload로 등록되고 프로젝트 부팅 시 ready다.
- 새 게임, load, SESSION reset의 소유자와 호출 순서가 코드와 문서에 일치한다.
- 외부 저장 계층이 사용할 snapshot adapter 계약이 있다.
- 실제 file/slot 저장은 Store 밖이며 별도 후속 Task로 남아 있다.
- 통합/실패/회귀 테스트가 자동화됐다.
- 설계 리뷰 및 최종 코드 리뷰에서 P0/P1이 없다.

## Follow-ups

- SaveGame file/slot system
- schema migration/key alias ADR
- ConditionSet + ConditionEvaluator
- State Read Data node
- Set/Add State Effect node
- conditional Choice / Response Selector
- State Inspector / DialogueHistory

## Related

- [[World-State-System]]
- [[World-State-User-Guide]]
- [[DT-005-StateSchema-WorldStateStore]]
- [[DT-005-WorldState-Review]]
- [[ADR-006-Typed-World-State]]
- [[Open-Tasks]]
