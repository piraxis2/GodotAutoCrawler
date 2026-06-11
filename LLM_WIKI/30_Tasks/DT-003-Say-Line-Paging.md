---
id: DT-003
type: task
status: completed
system: DialogueTool
created: 2026-06-11
updated: 2026-06-11
tags: [task, dialogue-tool, say, ui]
---

# Say Line Paging

## Goal

하나의 Say 텍스트에 줄바꿈이 있을 때 이전 줄을 같은 대화창에 유지하면서 한 줄씩 누적 공개하고,
마지막 줄을 모두 읽은 뒤에만 다음 Flow 노드로 진행한다.

## Context

기존 DialogueUI는 Say 전체 텍스트를 한 번에 타이핑하고, 완료 후 클릭하면 다음 노드로 진행했다.
초기 변경은 줄마다 텍스트를 교체해 이전 줄이 사라졌으나, 요구사항에 맞게 같은 RichTextLabel에
이전 줄을 유지하고 다음 줄을 누적하는 방식으로 교정했다.

## Scope

- `\n`, Windows `\r\n`, 단독 `\r`을 줄 경계로 처리한다.
- 첫 줄부터 타이핑한다.
- 타이핑 중 클릭하면 현재 공개 대상 줄까지 즉시 완성한다.
- 완료 상태에서 클릭하면 다음 줄을 기존 텍스트 아래에 추가하고 그 줄만 타이핑한다.
- 마지막 줄 완료 후 클릭해야 `DialoguePlayer.advance()`를 호출한다.
- 빈 줄도 줄 경계로 보존한다.
- Choice 전환, 대화 종료와 새 대화 시작 시 줄 진행 상태를 초기화한다.

## Out of Scope

- DialoguePlayer와 Say runtime request 형식 변경.
- Say Definition 및 Editor Adapter 변경.
- 페이지당 최대 줄 수, 스크롤 또는 자동 넘김.
- 음성 재생과 타이핑 속도의 동기화.

## Changes

- `addons/dialogtool/UI/dialogue_ui.gd`
  - `_say_lines`, `_say_line_index`, `_say_visible_text` 상태를 추가했다.
  - Say 문자열의 줄바꿈을 정규화하고 첫 줄부터 표시한다.
  - 다음 줄을 기존 텍스트에 누적하고, 마지막 줄 이후에만 Player를 진행한다.
  - Choice, 종료와 재생 시작 시 줄 상태를 정리한다.
- `addons/dialogtool/UI/type_effect.gd`
  - `start()`가 문자 타이머를 초기화한다.
  - 이전 줄까지 공개된 문자 수에서 타이핑을 재개하는 `start_from_visible_characters()`를 추가했다.

## Interaction Contract

두 줄 Say에서 클릭 흐름은 다음과 같다.

```text
첫 줄 타이핑
  -> 클릭: 첫 줄 즉시 완성
  -> 클릭: 첫 줄을 유지하고 둘째 줄 타이핑 시작
  -> 클릭: 둘째 줄 즉시 완성
  -> 클릭: 다음 Flow 노드
```

한 줄 Say는 기존과 동일하게 현재 문장 완성 후 다음 클릭에 Flow를 진행한다.

## Verification

- `git diff --check`: 통과.
- 정적 검토:
  - 이전 줄 누적 유지.
  - CRLF/CR 정규화.
  - 빈 줄 보존.
  - Choice/종료/새 재생 시 상태 초기화.
  - 한 줄 Say의 기존 클릭 흐름 유지.
- 현재 실행 환경에서 Godot 실행 파일을 찾지 못해 headless 및 실제 클릭 UI 검증은 수행하지 못했다.

## Follow-ups

- Godot 실행 환경에서 한 줄/여러 줄/빈 줄/CRLF Say의 실제 클릭 회귀 검증.
- 긴 누적 텍스트가 대화창 높이를 넘을 때의 스크롤 또는 페이지 제한 정책은 별도 UX 작업으로 다룬다.

## Related

- [[DialogueTool]]
- [[DialogueTool-Architecture]]

