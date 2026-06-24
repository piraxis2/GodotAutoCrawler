---
id: ADR-004
type: decision
status: accepted
date: 2026-06-11
system: DialogueTool
---

# Portrait MVP는 texture_path를 저장한다

## Context

[[DT-002-Portrait-State]]는 Portrait를 Say와 분리된 UI 상태 명령으로 도입한다.
Portrait 명령이 무엇을 식별자로 저장할지에 두 후보가 있었다.

- `actor` id를 저장하고 actor database/resolver가 텍스처를 해석한다.
- 리소스 `texture_path` 문자열을 직접 저장한다.

actor database와 resolver는 [[DT-002-Portrait-State]]에서 MVP 이후로 미뤄졌다.
Step 1은 런타임 명령 계약만 다루며 실제 렌더링과 상태 소유는 이후 Step의
DialogueUI 책임이다.

## Decision

Portrait MVP는 `texture_path` 문자열을 1차 식별자로 저장하고 전달한다.

- `portrait_state` 요청은 `texture_path`를 그대로 포함한다.
- `actor`와 `expression`은 향후 resolver를 위한 메타데이터로 함께 전달하되,
  현재 단계에서는 해석하지 않는다.
- `portrait_show`의 `texture_path`가 비어 있어도 Flow를 중단하지 않는다.
  경고를 남기고 요청은 그대로 발행한다. 이후 resolver가 `actor`/`expression`으로
  텍스처를 해석할 여지를 남긴다.

## Consequences

### Positive

- actor database 없이도 Portrait 명령을 실행하고 검증할 수 있다.
- 요청 형식이 `actor`/`expression`을 이미 포함하므로, resolver 도입 시
  요청 계약을 깨지 않고 식별자 우선순위만 바꾸면 된다.

### Negative

- 같은 actor의 텍스처 경로가 리소스마다 중복 저장될 수 있다.
- 텍스처 경로 변경 시 일괄 갱신이 어렵다. resolver 도입으로 완화한다.

## Related

- [[DT-002-Portrait-State]]
- [[DialogueTool]]
- [[ADR-001-Runtime-Snapshot]]
