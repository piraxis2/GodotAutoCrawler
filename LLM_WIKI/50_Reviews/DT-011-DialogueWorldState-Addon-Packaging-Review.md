---
id: DT-011-Review
type: review
task: DT-011
status: completed
date: 2026-06-17
system: DialogueTool, WorldState
---

# DT-011 DialogueWorldState Addon Packaging Review

## 발견 사항

P0/P1/P2 발견 사항 없음.

ADR-011의 핵심 결정과 실제 파일 상태를 대조했다. `addons/dialogtool/` 루트 유지, `world_state/` 하위모듈
포함, copy가 아닌 move, example-only schema, 호스트 autoload 등록, SaveGame 경계 불변이 현재 작업트리와
일치한다.

## 검토 내용

- `project.godot` autoload가 `DialogueManager`, `WorldState`, `WorldStateRuntime`를 addon 내부 경로로
  가리키는지 확인했다.
- `addons/dialogtool/world_state/world_state_store.tscn`이
  `addons/dialogtool/examples/world_state_schema_example.tres`를 참조하는지 확인했다.
- `addons/dialogtool/examples/affinity_ge_10.tres`와
  `addons/dialogtool/examples/sample_dialogues/sample_world_state_dialogue.tres`의 ext_resource 경로가 addon 내부
  리소스를 가리키는지 확인했다.
- 원본 `Assets/Script/gds/world_state` 디렉터리가 없는지 확인했다.
- 제품 코드/리소스에서 stale path
  (`res://Assets/Script/gds/world_state`, `res://addons/dialogtool/Test`, `res://test.tres`,
  `world_state/world_state_schema.tres`)를 검색했다. README migration 설명의 과거 경로만 의도적으로 남아 있다.
- `addons/dialogtool/README.md`가 설치 절차, headless/CI `DialogueToolUtil` 주의, runtime autoload 등록,
  game schema 교체, 기존 프로젝트 migration, SaveGame 경계를 문서화하는지 확인했다.

## 검증 결과

- Godot 4.6.3 mono headless `--import`: exit 0, parse/class error 없음.
- 선택 회귀 4/4 PASS:
  - `addons/dialogtool/world_state/tests/dt006_step1_bootstrap_test.tscn`
  - `addons/dialogtool/world_state/condition/tests/dt007_step4_e2e_test.tscn`
  - `addons/dialogtool/RunTime/tests/dt008_step3_branch_e2e_test.tscn`
  - `addons/dialogtool/RunTime/tests/dt009_step4_e2e_completion_test.tscn`

## 검증하지 못한 내용

- 이번 리뷰에서 fresh-project 수용 테스트를 새로 만들지는 않았다. Step 4 구현 보고의 fresh-project 7/7 PASS를
  이력 증거로 유지하고, 현재 작업트리에서는 핵심 경로·참조·선택 회귀를 재검증했다.
- 실제 GUI에서 addon 복사 후 플러그인 활성화 흐름은 수동으로 재검증하지 않았다.

## 잔여 위험

- `--import` 종료 시 Godot resource leak 경고가 출력됐다. parse/class/import 실패는 아니며, 패키징 완료를 막는
  DT-011 문제로 분류하지 않았다.
- README의 headless/CI `DialogueToolUtil` 수동 등록 주의는 기존 parse-time 의존을 문서로 보완한 상태다.

## 판정

**완료**.

DT-011 completion criteria를 충족한다. `DialogueTool + WorldState`는 한 addon 경계 안에 들어갔고, game-specific
schema/save data는 addon 코드와 분리됐으며, 기존 DT-004~009 핵심 경로는 경로 이동 후에도 유지된다.
