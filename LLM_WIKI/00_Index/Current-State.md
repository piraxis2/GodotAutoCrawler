---
type: status
project: AutoCrawler
updated: 2026-06-16
---

# Current State

## Project

- Godot 4.6.3 Mono, C#, .NET 8 기반 자동 턴제 택티컬 RPG 프로토타입이다.
- 전투 핵심은 Article, Status, BehaviorTree, TurnAction, TurnHelper로 구성된다.
- 실제 전투 실험 중심 씬은 `Assets/Scenes/Map/battle_field.tscn`이다.
- `main.tscn`은 UI와 윈도우 실험 성격이 강하다.

## DialogueTool

- Step 1~8 구현 및 리뷰가 완료됐다.
- 에디터 그래프는 `nodes/connections`, 런타임은 `runtime_nodes/runtime_connections`를 사용한다.
- 런타임 실행기는 Start, Say, Choice, Branch, End, Variable, Expression을 처리한다.
- 에디터 UI 구현은 Editor Adapter로 이동했다.
- `current_node_changed`와 원격 디버거 메시지를 통해 실행 노드 하이라이트가 가능하다.
- `DialogueManager.play(resource)`로 게임 코드에서 대화를 실행할 수 있다.
- 연속 대화와 이전 UI의 지연 종료 signal에 대한 재진입 방어가 적용됐다.
- Say 텍스트에 줄바꿈이 있으면 이전 줄을 같은 대화창에 유지하면서 한 줄씩 누적 공개하고,
  마지막 줄 이후에만 다음 Flow로 진행한다([[DT-003-Say-Line-Paging]]).

## World State

- 타입 안전 World State 기반 DT-005 Step 1~6이 완료됐다([[DT-005-WorldState-Review]], 판정: 완료).
- `addons/dialogtool/world_state/`: `StateDefinition`/`StateSchema`(선언·validation·lookup),
  `WorldStateStore`(read/write/reset/`value_changed`, SAVE/SESSION lifetime + `reset_lifetime`,
  JSON snapshot export/import replace-load, atomic `apply_batch`, read/mutation provider facade).
- `DialoguePlayer`는 read 상태 provider를 주입받는다(`DialogueManager`→`DialogueUI`→`DialoguePlayer`).
  `/root`/PlayerData/save를 직접 조회하지 않고 주입 provider로만 상태를 읽는다.
- 허용 타입은 bool/int/float/String/StringName, snapshot INT는 JSON-safe `±(2^53-1)`로 제한.
- 헤드리스 테스트 `addons/dialogtool/world_state/tests/dt005_step1~6_*`로 검증(ALL PASS).
- 실제 Schema 작성과 Store API 사용법은 [[World-State-User-Guide]]에 정리돼 있다.
- 런타임 통합 DT-006 Step 0~5 완료([[DT-006-WorldState-Runtime-Review]], 판정: 완료).
  - `WorldState` autoload(`/root/WorldState`): 유효 6-key bootstrap Schema(`world_state_schema.tres`)로
    부팅 시 ready.
  - `WorldStateRuntime` autoload(`/root/WorldStateRuntime`): new game/load lifecycle coordinator.
    `start_new_game()`/`restore_game()`(=`restore_world_state()`)는 transactional(envelope pre-validation,
    실패 시 기존 상태 보존), `capture_world_state()`는 SAVE-only. SESSION은 새 게임/load에서만 default.
  - 결정: [[ADR-007-WorldState-Runtime-Lifecycle]]. autoload 이름은 class_name 충돌 회피로 `WorldState`.
- State Condition Dialogue 통합은 DT-008에서 완료됐다(아래 DT-008 항목,
  [[DT-008-State-Condition-Dialogue-Integration]]). State Set/Add Effect와 명시적 mutation provider는
  DT-009에서 완료됐다([[DT-009-State-Mutation-Dialogue-Effects]], [[DT-009-State-Mutation-Review]],
  [[ADR-010-State-Mutation-Dialogue-Effects]] accepted). 미구현(후속): 실제 SaveGame file/slot 시스템
  (DT-006 adapter 소비), State Read Dialogue 노드.
  Step 1 구현·리뷰 완료: `WorldStateStore.add_state(key, delta) -> Dictionary`
  보고형 원자 Add API. INT/FLOAT strict, JSON-safe 도메인·overflow 거부, 값/signal 불변 실패, 실제 변경 시
  `value_changed` 1회. `dt009_step1_add_state_test` 20케이스 ALL PASS.
  Step 2 구현·리뷰 완료: `DialogueManager/UI.play(resource, read, mutation)` 세 번째 선택 인자 +
  `DialoguePlayer._mutation_state_provider`(read 권한 자동 승격 없음). `_run_effects`가 타입 디스패치로
  `portrait_*`는 UI 요청, `state_set`/`state_add`는 mutation provider 호출 + `state_mutation_evaluated`
  report signal(commit 후 1회, deep copy). provider 계약 = `{apply_state_batch, add_state}` duck-type 검증,
  누락(genuine null)은 `provider_missing`, 공급됐지만 못 쓰는 provider(freed/non-Object/arity/반환형 위반)는
  `provider_contract_invalid`, 모두 Flow 계속(SCRIPT ERROR 없음 — 호출 전 typeof/is_instance_valid/reflection
  arity+인자타입+typed array 원소타입(`Array[Dictionary]`만), 호출 후 반환 Dictionary+스키마 검증). mutation
  provider는 effect chain 시작 시 고정(listener 교체 무영향). Set은 `apply_state_batch` 재사용(authoritative diff),
  Add는 `add_state`. `EFFECT_TARGET_TYPES`에 `state_set/state_add` 추가(런타임·에디터 공유).
  `dt009_step2_runtime_mutation_test` 22케이스 ALL PASS, DT-004/005/006/008 회귀 ALL PASS.
  Step 3 구현·리뷰 완료: `StateSetDef`/`StateAddDef`(공통 추상 `StateEffectDef`, 비대기 leaf Effect
  노드) + 공유 `state_effect_editor_adapter`(key + type OptionButton + value/delta LineEdit + Effect 입력 포트).
  StateSet=5타입, StateAdd=INT/FLOAT만. literal은 capture에서 엄격 파싱(잘못된 입력은 String으로 보존),
  저장 검증이 타입 불일치를 차단하고 런타임은 변환 없이 Store가 `type_mismatch` 거부(조용한 0/false 변환 없음).
  `node_type_registry` 등록, 노드 목록 자동 노출("StateSet"/"StateAdd"). `dt009_step3_editor_roundtrip_test`
  (실제 `dialoguetool_main.tscn` fixture, A~F) ALL PASS: 목록 노출, 포트, capture→save→reload→recapture에서
  Definition typeof 직접 단언/연결(Say 소스 포함) 보존, authored 그래프 런타임 실행(gold 100→set 200→add 205),
  잘못된 literal 저장 차단+런타임 거부.
  Step 3b 구현·리뷰 완료: **Choice 항목별 Effect authoring**. Choice 출력 포트 = flow + 항목별 effect +
  **전용 공통 effect 포트**(flow/data index 보존). 항목별 연결만 `choice_index` 보존, 선택 시 해당 항목 + 공통
  Effect만 실행. 공통 연결은 전용 공통 포트로 정규화돼 저장/재로드/재캡처 후에도 choice_index 없이 유지(항목0
  오염 방지 — Step 3b 리뷰 P1 수정), 잘못된 choice_index는 fallback 없이 건너뜀. resize 시 항목별·공통 연결
  모두 remap 보존. `dt009_step3b_per_choice_effect_test`(A~G) ALL PASS, DT-004/DT-008 Choice 회귀 유지.
  Step 4 구현·리뷰 완료(제품 코드 변경 없음): 실제 `DialogueManager→UI→Player→WorldStateStore`
  전체 경로 e2e(`dt009_step4_e2e_completion_test` A~G)로 Choice 선택→항목별 mutation→Branch(state_condition)가
  변경값을 즉시 읽는 흐름, 반복/latest-wins(폐기 provider mutation 0회)/provider 누락/read-only 실패(값 불변+Flow
  계속)/에디터 authored 왕복 실행을 검증. 전체 회귀 matrix 30 scene ALL PASS(DT-004~009), `--import` 0 오류.
- DT-007 Step 1~4 구현·검증·리뷰 완료([[DT-007-Condition-Review]], 판정: 수정 후 완료).
  - `addons/dialogtool/world_state/condition/`: `ConditionClause`(@abstract base),
    `StateCondition`(leaf), `ConditionGroup`(ALL/ANY/NOT, recursive `Array[ConditionClause]`),
    `ConditionSet`(top-level asset), `ConditionValidator`(구조 검증), `ConditionEvaluator`(pure-read 평가).
  - Step 1 validator는 iterative DFS로 null/unknown/empty/NOT arity/cycle/alias/depth(64)/node(4096)/key/
    operator/expected/ordered 타입을 검사하고 `{valid, errors[{code,path,key,message}], error_codes,
    node_count}` deep copy를 반환한다. provider read 없음. spike로 `@abstract` 인식 + 재귀 `.tres` 왕복 확인.
    추가 코드 `logic_invalid` 코드 리뷰 비준됨. 판정: Step 1 완료.
  - Step 2 evaluator는 2단계 평가다: validator(read 0)를 먼저 통과해야 주입 provider의 `has_state`/
    `read_state`만으로 트리를 재귀 평가한다. strict typeof 비교(암시적 변환 없음), evaluation-local key
    cache(miss 포함 1회 read), non-short-circuit 전체 trace, fail-closed errored 전파(NOT/ANY가 errored
    child를 pass로 안 바꿈), `{passed, valid, errors, trace, read_count}` deep copy. mutation/autoload 미접근.
  - Step 3은 실제 `WorldStateStore`를 read provider로 주입하는 통합 검증(제품 코드 변경 없음). Step 4는
    end-to-end 완료 검증: 대표 RPG `.tres`를 재로드해 in-memory set과 **동일한 report**(passed/valid/
    errors/trace/read_count 문자열 표현 일치)를 실제 Store에서 내고, load lifecycle(restore_world_state로
    SESSION reset 직접 단언)·성능 sanity(node 4096, 같은 key read 1)·fail-closed Store 불변을 확인했다.
  - 검증: `condition/tests` `dt007_step1`(24)/`step2`(23)/`step3`(11)/`step4`(e2e)/`spike` ALL PASS.
    전체 회귀 DT-004(5)+DT-005(6)+DT-006(5) ALL PASS, editor `--import` 0 오류 — 합계 21 headless.
  - 후속 State Condition Dialogue node 입력 계약은 [[DT-007-Condition-Review]]에 문서화했다.
    [[DT-008-State-Condition-Dialogue-Integration]] Step 0은 Approved after design fixes로 승인됐고,
    [[ADR-009-State-Condition-Dialogue-Consumption]]을 accepted로 확정했다.
- DT-008 Step 1(Runtime State Condition Data Node) 구현·리뷰 완료(판정: 수정 후 완료)
  ([[DT-008-State-Condition-Dialogue-Integration]] Step 1 결과).
  - `WorldStateConditionDef`(Data Definition, runtime type `state_condition`)가 `ConditionSet`을
    runtime params로 보존한다. `DialoguePlayer._get_data_value(node_id, consumer_node_id, visited)`의
    `state_condition` 분기가 주입된 원본 `_read_state_provider`를 `ConditionEvaluator.evaluate`에 직접
    전달하고(facade 재포장 금지) `report.passed`를 boolean Data로 반환한다.
  - `condition_evaluated(condition_node_id, consumer_node_id, report)` signal을 평가당 1회 발행한다.
    consumer는 입력 포트를 직접 소유한 노드(Branch=branch id, expression 중첩=expression id)다.
    동기 signal listener가 분기를 못 바꾸도록 `passed`를 발행 전에 캡처하고 signal에는 `report.duplicate(true)`
    deep copy를 넘긴다(1차 리뷰 P1 수정). provider 미지정/null·invalid set/missing key/타입 오류는 모두
    false이고 구조 오류는 read_count==0이다.
  - 헤드리스 `addons/dialogtool/RunTime/tests/dt008_step1_state_condition_test`(15 사례, P1 회귀 O 포함)
    ALL PASS.
- DT-008 Step 2(Editor Authoring and Resource Round-trip) 구현·리뷰 완료(판정: 수정 후 완료).
  - `WorldStateConditionNode`(전용 GraphNode 씬) + `condition_set_picker`(ConditionSet `.tres` 드롭) +
    `world_state_condition_editor_adapter`(boolean output 슬롯 + capture/apply). `node_type_registry`에
    `state_condition` 어댑터 등록. boolean output은 Branch boolean 조건 입력과 동일 타입이고 editor.gd의
    `data↔boolean` 교차 호환으로 data 입력에도 연결된다.
  - 선행 F4 spike(`dt008_step2_snapshot_spike`)로 `runtime_nodes` Dictionary에 2중 중첩된 ConditionSet이
    external(`ext_resource`)/inline(`sub_resource`) 양쪽에서 `.tres` 왕복 보존됨을 확인(Design Deviation
    없음). 에디터 왕복 `dt008_step2_editor_roundtrip_test`로 외부 참조/연결 capture→save→재로드 보존,
    null 저장+런타임 fail-closed를 검증. 1차 리뷰 P2(빈 노드 노출)는 정식 노드 등록으로 해소.
  - 전체 회귀(DT-008 step1, DT-004 step1~4, DT-005 step5, DT-007 step1~4) + editor import ALL PASS.
    Branch e2e는 Step 3, 조건부 Choice는 Step 4~5 범위다.
- DT-008 Step 3(Branch End-to-End Integration) 구현·리뷰 완료(판정: 수정 후 완료). **제품 코드 변경 없음**(통합
  검증 단계). 실제 `DialogueManager.play(resource, store)` → UI → Player provider 주입 경로에서 통합
  그래프 `Start → Branch(state_condition) → Say TRUE/FALSE → End`가 실제 `WorldStateStore` 값에 따라
  분기함을 검증했다(`dt008_step3_branch_e2e_test`): set/reset/snapshot restore 후 Store 최종값 일치,
  provider 미지정/조건 오류 false Flow(크래시·자동 true 없음), condition_evaluated node/consumer/report
  검증, 반복·같은 프레임 교체(latest-wins) provider 미혼입. DT-004/005/006/007/008 전체 24 headless
  ALL PASS.
- DT-008 Step 4(Conditional Choice Runtime Mapping) 구현·리뷰 완료(판정: 수정 후 완료).
  `DialoguePlayer`가 Choice 진입 시 항목별 Data 입력(port i+1)을 조건으로 평가해 visible list와
  `_choice_visible_map`(visible_index → 원래 항목 index = 원래 flow 출력 port)을 구성한다. Data 입력 없으면
  항상 표시(레거시 호환), 조건은 진입 시 1회만 평가하고 대기 중 재평가하지 않으며 재진입에서만 갱신.
  `select_choice(visible_index)`는 mapping 범위를 먼저 검증해 범위 밖이면 대기 유지(Flow 불변), 통과 시에만
  원래 port로 effects/Flow 커밋(F5). all-hidden 명시 종료, invalid/error 조건 숨김, no-input 레거시는 identity.
  내부 Data 평가는 `_eval_data → {value, errored}`로 전파되어, errored 조건이 Expression(`not c`/`c or true`)을
  통해 true로 뒤집히지 못하고 Branch/Choice에서 fail-closed된다(1차 리뷰 P1 수정, ADR-008 error-dominance).
  헤드리스 `dt008_step4_conditional_choice_test`(L1~L4 error-dominance 포함) + DT-004 Choice 회귀 ALL PASS.
- **DT-008 Step 0~5 완료**([[DT-008-Choice-Integration-Review]], 판정: 완료 — Step 1~4 수정 후 완료,
  Step 5 Approved after design fixes). State Condition Data 노드(`state_condition`)가 boolean Data로 Branch와
  조건부 Choice를 같은 `ConditionSet`/`ConditionEvaluator` 계약으로 제어한다.
- DT-008 Step 5(Conditional Choice Editor and Completion Review) 완료.
  **제품 코드 변경 없음**(검증 + 문서). 실제 `dialoguetool_main.tscn` fixture로 (1) State Condition boolean
  output ↔ Choice 항목별 Data 입력 연결이 저장/재로드 후 동일하고 Choice resize(3→2)가 남은 항목의 조건/Flow
  연결을 잘못 재배치하지 않으며 사라진 항목 연결만 드롭함을 검증하고, (2) 복합 `Branch(state_condition) +
  conditional Choice`가 실제 `WorldStateStore` 상태에 따라 같은 evaluator 계약으로 동작함을 e2e로 확인했다
  (`dt008_step5_completion_test`). 전체 회귀 26/26 GREEN(DT-004~008), editor `--import` 0 오류.
  Step 1~4 판정 수정 후 완료, Step 5 Approved after design fixes([[DT-008-Choice-Integration-Review]]).
- DT-010 deferred: DialogueTool 에디터 Play에서 WorldState provider를 주입해 `WorldStateCondition`과
  `StateSet`/`StateAdd`를 직접 확인하는 debug preview 작업([[DT-010-Dialogue-Debug-WorldState-Preview]]).
  현재 에디터 Play는 별도 Godot 프로세스에 `--dialogue_resource`만 넘기고 `DialoguePlayer.start_dialogue()`를
  호출하므로 read/mutation provider가 주입되지 않는다. 다만 다른 프로젝트 재사용을 위해
  [[DT-011-DialogueWorldState-Addon-Packaging]]을 먼저 진행하고, 새 addon 구조 기준으로 재개한다.
- DT-011 Step 0 설계 리뷰 완료(판정: Approved after design fixes,
  [[ADR-011-DialogueWorldState-Addon-Packaging]] accepted). 결합 표면 확정: 제품 코드 결합은 condition
  `class_name` 하나(경로 독립)뿐, mutation/store/runtime은 provider 주입으로 decoupled. 결정: 후보 B
  (`addons/dialogtool/world_state/` 하위모듈), 런타임 autoload 3종은 호스트 수동 등록(순서 보장),
  addon은 example schema만 포함.
- **DT-011 Step 1 구현 완료 — 리뷰 대기.** WorldState 폐쇄집합(state_definition/state_schema/store
  (.gd/.tscn)/runtime/condition/* + WorldState·Condition tests)을 `git mv`로
  `Assets/Script/gds/world_state/` → `addons/dialogtool/world_state/`로 **이동**(복사·shim 없음,
  `.uid` 동반, 원본 디렉터리 제거 확인). path rewrite: `project.godot` autoload 2종, store.tscn/
  schema.tres ext_resource, 이동한 테스트 `.tscn`/`.tres` ext_resource + `.gd` const
  (`SCHEMA_PATH`/`STORE_SCENE`/`RUNTIME_SCRIPT`/`CLAUSE_SCRIPT`), addon dt008_step1/3/5 `SCHEMA_PATH`,
  `affinity_ge_10.tres`(Step 1 당시 `addons/dialogtool/Test/`, Step 2에서 `examples/`로 이동) condition
  ext_resource. schema.tres uid
  `uid://urle8xa2dmc` 보존. headless `--import` 0 parse 에러 + class_name 중복 0, 회귀 20 scene
  (DT-005×6, DT-006×5, DT-007×5, DT-008 step1/3/5, DT-009 step4) ALL PASS. 제품 코드/테스트/리소스에
  stale path 0(`.godot`/`.idea` 캐시 제외).
- **DT-011 Step 2 구현 완료 — 리뷰 대기.** dialogtool path 정규화. addon 테스트 `SCHEMA_PATH`는 Step 1에서
  이미 정규화됐고, 고유 작업은 example ConditionSet 이동: `addons/dialogtool/Test/affinity_ge_10.tres`
  → `addons/dialogtool/examples/affinity_ge_10.tres`(`git mv`, uid `uid://bwsq70tpasvaw` 보존). 비게 된
  `Test/` 디렉터리 제거, 루트 샘플 `test.tres`의 ext_resource path 갱신. 파일 내부 condition ext_resource는
  Step 1에서 이미 새 경로. headless `--import` 0 parse 에러, DT-004/008/009 회귀 15 scene
  (DT-004 step1~4+pipeline, DT-008 step1~5+spike, DT-009 step2/3/3b/4) ALL PASS.
- **DT-011 Step 3 구현 완료 — 리뷰 대기.** examples/migration/docs. (1) example schema 개명: `git mv`
  `world_state/world_state_schema.tres` → `examples/world_state_schema_example.tres`(uid `uid://urle8xa2dmc`
  보존), store.tscn + 테스트 6개 `SCHEMA_PATH` 재작성. store.tscn이 example을 가리켜 out-of-box 부팅 유지,
  게임 schema는 호스트가 교체(ADR-011 D5). (2) sample dialogue: 루트 `test.tres` 채택 →
  `examples/sample_dialogues/sample_world_state_dialogue.tres`(state_condition 분기 + state_add(+50 affinity)
  데모, example schema·ConditionSet 소비). (3) `addons/dialogtool/README.md` 신규: 설치(autoload 순서
  DialogueManager→WorldState→WorldStateRuntime, DialogueToolUtil은 플러그인 자동등록)·게임 schema 교체·기존
  프로젝트 마이그레이션 문서. headless `--import` 0 parse 에러, 명시적 load check(sample 8 nodes/schema
  valid 6 keys/ConditionSet ok) + schema 의존 회귀 8 scene ALL PASS.
- **DT-011 Step 4 구현 완료 — 완료 판정 대기(제품 코드 변경 없음).** 통합 matrix + 수용 검증. 전체 DT-004~009
  **32/32 scene ALL PASS** + `--import` 0 parse 에러(stale 경로 0, `.godot`/`.idea` 캐시 제외). **Fresh-project
  수용 테스트**: 임시 빈 프로젝트에 `addons/dialogtool/`만 복사+autoload 등록+plugin enable → `--import` 0 에러
  → 수용 회귀 7/7 PASS(autoload boot, ConditionSet 분기, StateAdd/Set, Choice→state_add→Branch 전체 경로) →
  검증 후 삭제. autoload 실패 시나리오 크래시 0(잘못된 순서도 Godot 4.6.3 batch _ready로 store 해석됨, WorldState
  누락 시 graceful not-ready, provider_missing fail-closed). **발견**: `dialogue_player/manager.gd`가
  `DialogueToolUtil` autoload에 parse-time 의존(기존 설계) → 순수 헤드리스/CI 설치는 이 autoload도 등록해야
  parse됨 → README "헤드리스/CI 주의"로 문서화. DT-010 재개 구조 확정(addon 내부 example provider로 자급).
  **DT-011 Step 1~4 구현·검증 완료, 최종 완료 판정 대기.**

## Known Gaps

- Portrait는 `Say` 요청의 문자열 필드와 별개로, 독립적인 비대기 UI 상태 명령으로 분리됐다.
  DT-002 Portrait State MVP가 완료됐다([[DT-002-Portrait-Review]]).
  - DialoguePlayer가 `portrait_show/hide/expression`을 비대기 `portrait_state` 요청으로 발행한다(Step 1).
  - 세 노드를 에디터에서 생성·편집하고 Definition/runtime snapshot에 저장·재로드한다
    (공통 `PortraitDef` + `portrait_editor_adapter`, Step 2).
  - DialogueUI가 left/center/right 슬롯에서 Portrait 상태를 소유하고 `texture_path` Texture를
    렌더링한다. Say/Choice 전환에도 유지되고 종료/교체 시 정리된다(Step 3).
  - 반복 실행/교체/재진입 수명주기와 기존 리소스 호환을 통합 검증했다(Step 4).
  - MVP 이후 후속: transition 애니메이션, Portrait Focus/dim, actor database/resolver,
    speaker 기반 자동 선택. [[Open-Tasks]] 참고.
- Autoload와 SceneFunction의 안전한 런타임 평가/부작용 정책은 미완성이다.
- DialogueTool 헤드리스 자동 테스트가 고정됐다(`dt004_step1~4`+integration). World State는
  `dt005_step1~6`로 통합 매트릭스까지 검증된다. 별도의 에디터-저장 기반 통합 회귀 .tres 샘플은 아직 없다.
- Say 줄 누적 표시는 정적 검토만 완료됐으며 Godot 실제 클릭/headless 회귀 검증이 남아 있다.
- Portrait와 주 Flow를 같은 실행 지점에 연결하는 비대기 Effect 모델이 완료됐다
  ([[DT-004-Nonblocking-Effect-Flow]], [[ADR-005-Nonblocking-Effect-Connections]], [[DT-004-Effect-Flow-Review]]).
  한 Flow 출력의 다중 주 Flow 대상은 저장 validation으로 차단된다(런타임은 여전히 첫 주 Flow만 실행).
  - Step 1(런타임 계약) 완료: connection의 `kind: "effect"`로 Effect 연결을 식별한다.
    `get_runtime_next_node_id`는 Effect를 건너뛰어 주 Flow만 반환하고, `get_runtime_effect_node_ids`가
    Effect 대상을 저장 순서대로 반환한다. DialoguePlayer가 노드를 떠나는 시점에 Effect들을 비대기로
    발행하며, 순환·잘못된 대상·누락을 경고 후 건너뛴다. Portrait 타입만 Effect 대상으로 허용한다.
  - Step 2(에디터 포트/저장·재로드) 완료: `port_type`에 `effect`(전용 색상) 추가.
    Start/Say에 Effect 출력 포트(port 1), Portrait에 Effect 입력 포트(port 1)를 두되 기존 Flow/Data
    port index는 보존한다. capture가 출력 포트 타입에서 `kind`를 파생해 저장/재로드/재캡처에 보존한다.
    load 시 `kind=="effect"` 연결은 노드의 Effect 포트로 정규화한다(레거시 0→0 리소스 호환).
    런타임 Effect 식별은 port-agnostic(`kind` 기준)으로 조정했다.
  - Step 3(validation·편집 UX) 완료: 저장 validation이 (A) 한 Flow 포트의 주 Flow 대상 2개 이상,
    (B) Portrait 아닌 Effect 대상, (C) Effect 순환을 거부하고, 오류 메시지에 node/type/port를 포함한다.
    Effect→Say는 effect↔flow 카테고리 불일치로 거부된다. Effect 포트는 주황색+라벨+tooltip로 구분한다.
    Effect 대상 화이트리스트는 `DialogueGraphResource.EFFECT_TARGET_TYPES` 단일 정의로 런타임과 공유한다.
    헤드리스 테스트로 런타임(`dt004_step1_*`), 에디터 왕복(`dt004_step2_*`), validation 행렬(`dt004_step3_*`)을 검증.
  - Step 4(통합 회귀·완료 판정) 완료: 두 Effect 지점 시나리오를 DialogueUI/DialogueManager로 실행해
    Effect와 Say/Choice 미간섭·중복 없음, 저장/재로드 왕복, 반복·교체·재진입 수명주기, 직렬/무 Portrait 회귀를
    헤드리스 5개 테스트(`dt004_step1~4_*`)로 검증했다. P0/P1 없음([[DT-004-Effect-Flow-Review]]).
- Definition이 Adapter 조회를 중계하는 점진적 호환 계층이 남아 있다.
- 전투 시스템에는 게임오버 후속 처리와 일부 null 방어 과제가 남아 있다.

## Verification Baseline

- Godot 4.6.3 headless editor load가 성공해야 한다.
- Dialogue 리소스는 편집 -> 저장 -> 재로드 후 값과 포트 순서를 보존해야 한다.
- `Start -> Say -> Choice -> Branch -> End` 흐름이 종료까지 실행돼야 한다.

## Related

- [[Open-Tasks]]
- [[DialogueTool-Architecture]]
- [[DialogueTool-Step-1-to-8]]
