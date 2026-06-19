---
type: task-index
project: AutoCrawler
updated: 2026-06-19
---

# Open Tasks

## Next



- DialogueManager 반복 실행/교체/연속 실행 테스트를 자동화한다.

## Later

- DT-010 옵션 C: 에디터 debug Play preview에서 고정 example schema 대신 게임 schema 경로를 debug 설정으로
  주입하는 toggle. parse-safe하게 구현 가능(autoload는 `get_node_or_null` 런타임 lookup,
  [[ADR-012-Dialogue-Debug-Preview-Provider]] D1/D2). 현재는 game schema key가 preview에서
  state_missing/unknown_key로 fail-closed됨([[DT-010-Dialogue-Debug-WorldState-Preview]] Step 3 한계).
- 노드 display name/alias 시스템: 현재 노드 목록/그래프 타이틀은 `class_name`에서 "Def"를 떼어 도출하므로
  공백 포함 표시 이름(예: "State Read")이 불가하다. 노드별 표시 이름을 Definition이 선언하게 하면
  `WorldStateRead` → "State Read"처럼 ADR 개념 명칭과 사용자 라벨을 일치시킬 수 있다(DT-013 Step 2 P3 후속).
- schema-aware key/operator picker, inline ConditionSet tree editor, condition trace inspector —
  DT-012 후속(현재는 외부 `.tres`/inline ConditionSet 지정 + provider-free readable summary 표시까지.
  편집 UI·schema 연동·평가 trace 시각화는 범위 밖).
- 조건 평가 trace inspector UI와 disabled-choice + reason UI — DT-008 `condition_evaluated` seam 소비.
- Response Selector와 weighted/random response
- DialogueHistory 및 State Inspector
- Portrait transition 애니메이션(fade/slide 등) — DT-002 MVP 이후.
- Portrait Focus와 비활성 Portrait dim 처리 — DT-002 MVP 이후.
- actor database 및 actor/expression -> Texture resolver — DT-002 MVP 이후.
- speaker 기반 자동 Portrait focus/선택 정책 — DT-002 MVP 이후.
- 기존 `SayDef.portrait` 데이터의 명시적 마이그레이션 도구.
- Set Variable 노드
- Compare 노드
- Random Branch 노드
- Narration 노드
- Emit Event 노드
- Wait 및 Sound 연출 노드
- Entry Point 또는 named entry 지원
- SaveGame 후속(SG-001~003 범위 밖): 실제 production save menu UI scene(SG-003은 host integration contract
  문서 + test-only fake host flow까지만, 실제 위젯/theme/localization/input focus는 host 소유),
  autosave/quicksave 구현, thumbnail/capture image, 다세대 백업 history, compression/encryption,
  schema/section version migration registry, Dialogue SaveEffect(저장 트리거는 game/event layer 우선).

## Recently Completed

완료 작업의 상세 사실/판정은 Current-State와 각 Review가 보존한다. 여기는 최근 완료 포인터만 둔다.

- **DT-015 Dialogue Integrated Regression Graph 완료**(Step 1~2, [[DT-015-Dialogue-Integrated-Regression-Graph-Review]] 판정: 완료): DialogueTool의 기본 대화 조합을 검증하는 canonical regression graph를 작성하고 Step 1(Runtime) 및 Step 2(Editor authored round-trip) 검증을 완료함.
- **WC-001 WorldCore Umbrella Migration 완료**(Step 1~4, [[WC-001-WorldCore-Umbrella-Migration]] 판정: 완료): `DialogueTool`, `WorldState`, `SaveGame` 및 관련 어댑터 모듈을 `addons/world_core/` 하위 sibling 구조로 안전히 이전하고, 모든 프로젝트 경로 및 리소스 내 구 경로 치환 완료. 18종 WorldState/SaveGame 및 23종 DialogueTool 회귀 테스트 ALL PASS.
- **DT-014 Say Line Paging UI Regression 완료**(Step 0~2, [[DT-014-Say-Line-Paging-UI-Regression-Review]] 판정: 완료): 실제 UI `Dialogue_UI.tscn` 클릭 경로에서 DT-003 Say 줄 누적 표시 기능의 headless 회귀 검증. 타이핑 효과의 `set_process(false)` 비활성화로 가변 프레임 델타로 인한 비결정성을 근본적으로 제거하였고, Case 6.2 Choice flow 출력 포트 오배선도 수정함.
- **DT-013 State Read Data 노드 완료**(Step 0~4, [[DT-013-State-Read-Data-Node-Review]] 판정: 완료): 단일 World
  State key 값을 strict typeof로 읽어 Branch/Choice/Expression에 공급하는 `state_read` leaf Data 노드. 주입
  read provider만 소비(fail-closed report + Data error-dominance), editor authoring/저장 validation은
  `StateSchema.KEY_PATTERN` 재사용. 노드 라벨은 "WorldStateRead"(display name/alias 후속은 위 Later).
  결정 [[ADR-015-State-Read-Data-Node]], 사실 [[DialogueTool]]/[[World-State-System]]/[[DialogueTool-User-Guide]].
- **SaveGame SG-001~003 완료**: core(`SaveSection`/`SaveGameManager`, slot save/load/list/delete + 한 세대
  백업/복구 + WorldState adapter) → `SaveFlow` facade(metadata provider + caller override + save gate) →
  host save slot UI integration contract(문서 + test-only fake host flow). 사용법 [[SaveGame-User-Guide]],
  현재 사실 [[SaveGame-System]], 판정 [[SG-001-SaveGame-Core-Section-System-Review]] /
  [[SG-002-SaveFlow-Facade-Metadata-Provider-Review]] / [[SG-003-SaveSlot-UI-Host-Integration-Review]].
  실제 production save menu UI 등 후속은 위 Later 참고.

## Deferred Architecture

- Definition의 Adapter 호출 중계 제거
- NodeTypeRegistry 기반 에디터 노드 팩토리화
- Autoload read와 write/effect 노드의 책임 분리
- SceneFunction 호출 대상, 인자, 반환값, 실패 정책 확정

## Maintenance

- 시스템 문서는 코드 변경 후 현재 사실만 남도록 갱신한다.
- 완료 작업은 Task 문서에 검증 결과를 남기고 이 목록에서 제거한다.
- 새로운 중요한 설계 선택은 ADR을 먼저 작성한다.
