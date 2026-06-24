---
id: ADR-003
type: decision
status: accepted
date: 2026-06-11
system: DialogueTool
---

# DialogueManager Autoload

## Context

게임 씬과 C# 코드에서 DialogueUI와 DialoguePlayer의 내부 구조를 직접 알아야 하면 결합도가 높아진다.

## Decision

`DialogueManager`를 project autoload로 등록하고 다음 API를 제공한다.

- `play(dialogue_resource)`
- `is_playing()`
- `dialogue_started`
- `dialogue_end`
- `ui_request`

Manager가 CanvasLayer와 DialogueUI의 생성 및 정리를 소유한다.

## Signal Lifetime Rule

- 종료 signal의 source UI가 현재 UI인지 확인한다.
- 현재 UI를 먼저 정리한 뒤 외부 `dialogue_end`를 emit한다.
- 종료 callback에서 다음 대화를 즉시 시작할 수 있어야 한다.

## Consequences

- 게임 코드는 대화 UI 구조를 알 필요가 없다.
- C#은 `/root/DialogueManager`에 `Call("play", resource)`할 수 있다.
- 동시에 여러 대화를 표시하지 않고 새 play가 기존 대화를 교체한다.

