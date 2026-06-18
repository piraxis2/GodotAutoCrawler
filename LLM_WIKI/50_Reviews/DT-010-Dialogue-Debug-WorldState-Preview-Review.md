---
id: DT-010-Review
type: review
task: DT-010
status: completed
date: 2026-06-17
system: DialogueTool, WorldState
---

# DT-010 Dialogue Debug WorldState Preview Review

## 발견 사항

P0/P1/P2 발견 사항 없음.

Step 3 구현은 ADR-012의 핵심 결정과 일치한다. debug preview provider는 addon example schema로 전용
`WorldStateStore`를 구성하고, `DialoguePlayer._ready()` debug 분기에서 `start_dialogue.call_deferred` 전에
read/mutation provider를 동기 주입한다. 일반 `DialogueManager.play(...)` runtime provider 계약은 변경하지
않는다.

## 검토 내용

- `addons/dialogtool/RunTime/dialogue_player.gd`: debug hint 분기에서 `_inject_debug_preview_provider()`가
  deferred self-start 전에 실행된다. 이미 provider가 있는 경우 덮어쓰지 않고, helper 실패 시 provider 미주입
  상태로 fail-closed한다.
- `addons/dialogtool/RunTime/dialogue_debug_preview_provider.gd`: `WorldStateStore`/`StateSchema` `class_name`과
  string path만 사용한다. `WorldState`/`WorldStateRuntime` bare autoload 식별자를 추가하지 않아 fresh-project
  parse-safety 요구를 유지한다.
- `addons/dialogtool/UI/Dialogue_UI.tscn` + `dialogue_ui.gd`: UI 씬은 child `DialoguePlayer`를 포함하고,
  parent UI `_ready()`에서 player signals를 연결한다. player의 deferred self-start 덕분에 첫 `ui_request`를
  놓치지 않는다.
- `addons/dialogtool/RunTime/tests/dt010_step3_editor_play_e2e_test.gd`: 실제 `Dialogue_UI.tscn`을 instantiate하고
  sample dialogue를 debug-hint self-start로 실행해 `Take -> Rich`, `Leave -> Poor`를 실제 label 렌더와
  provider state로 확인한다.
- `LLM_WIKI/20_Systems/DialogueTool-User-Guide.md`: editor Play preview 자동 주입, process isolation
  lifecycle, diagnostic log, 고정 example schema 한계를 설명한다.
- `LLM_WIKI/00_Index/Open-Tasks.md`: 옵션 C(게임 schema 경로 debug 주입)는 Later 후속으로 남겨 둔다.

## 검증 결과

- Godot 4.6.3 mono headless `--import`: exit 0, parse/class error 없음.
- 선택 회귀 5/5 PASS:
  - `addons/dialogtool/RunTime/tests/dt010_step1_debug_preview_provider_test.tscn`
  - `addons/dialogtool/RunTime/tests/dt010_step2_preview_lifecycle_test.tscn`
  - `addons/dialogtool/RunTime/tests/dt010_step3_editor_play_e2e_test.tscn`
  - `addons/dialogtool/RunTime/tests/dt008_step3_branch_e2e_test.tscn`
  - `addons/dialogtool/RunTime/tests/dt009_step4_e2e_completion_test.tscn`

## 검증하지 못한 내용

- 실제 GUI 클릭과 `--remote-debug` Output/Debugger 패널 전달은 헤드리스에서 직접 확인하지 못했다.
  User Guide와 Task에 수동 절차로 남겼다.

## 잔여 위험

- preview store는 고정 example schema만 사용한다. 게임 schema key로 작성한 대화는 현재 preview에서
  `state_missing`/`unknown_key`로 fail-closed한다.
- 옵션 C(게임 schema 경로 debug 주입)는 parse-safe하게 가능하지만 설정 UI, 우선순위, 검증이 별도 설계를
  요구하므로 Later 후속으로 둔다.
- `--import` 종료 시 Godot resource leak 경고가 출력됐다. parse/class/import 실패는 아니며 DT-010 완료를 막는
  문제로 분류하지 않았다.

## 판정

**완료**.

DT-010 completion criteria를 충족한다. DialogueTool debug Play 경로에서 WorldState read/mutation preview가
동작하고, 일반 runtime provider 계약과 실제 save state는 오염하지 않는다.
