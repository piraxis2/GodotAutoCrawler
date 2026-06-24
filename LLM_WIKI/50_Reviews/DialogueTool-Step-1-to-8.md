---
type: review
system: DialogueTool
status: complete
reviewed: 2026-06-11
---

# DialogueTool Step 1 to 8

## Scope

DialogueTool의 런타임 분리, Choice/Branch/Expression, Editor Adapter, 디버그 프리뷰, 게임 통합 API를 단계별로 구현하고 리뷰했다.

## Important Findings and Fixes

- 첫 `ui_request`가 UI signal 연결 전에 발생하던 문제를 deferred start로 수정
- Choice 텍스트 순서와 재로드 후 손실 문제를 ordered array와 UI 복원으로 수정
- 슬라이더 변경 시 기존 Choice 텍스트 보존
- Vector3 z 복원 index 오류 수정
- Expression 순환 data dependency 방어 추가
- 이전 UI의 지연 종료 signal이 새 대화를 닫는 문제를 source UI 검사로 수정
- 종료 callback에서 다음 대화를 시작하면 삭제되던 문제를 cleanup-before-emit 순서로 수정

## Accepted Debt

- Definition의 Adapter 호출 중계
- NodeTypeRegistry의 에디터 팩토리화 미적용
- Autoload/SceneFunction 런타임 정책 미완성
- 통합 자동 테스트 부족

## Verification

- Godot 4.6.3 headless editor load: PASS
- Choice load/capture round-trip: PASS
- DialogueManager 재진입 코드 리뷰: PASS
- Step 8 완료 판정

## Final Assessment

핵심 구조와 사용자 흐름은 다음 기능 개발을 진행할 수 있는 수준이다. 다음 기능은 [[STEP_REVIEW_WORKFLOW]]에 따라 별도 Task와 Review로 관리한다.

