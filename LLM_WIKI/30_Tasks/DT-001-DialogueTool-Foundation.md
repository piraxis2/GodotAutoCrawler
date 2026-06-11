---
id: DT-001
type: task
status: complete
system: DialogueTool
created: 2026-06-10
completed: 2026-06-11
tags: [task, dialogue-tool]
---

# DialogueTool Foundation

## Goal

미완성 DialogueTool을 편집기와 런타임이 분리된 실행 가능한 대화 시스템으로 만든다.

## Completed Steps

1. Start -> Say -> End 실행 루프
2. Choice 분기와 UI
3. runtime snapshot 저장 및 validation
4. Branch + Variable 평가
5. Expression evaluator와 순환 방어
6. Editor Adapter 분리
7. 실행 노드 디버그 하이라이트
8. DialogueManager 게임 통합 API

## Key Outcomes

- 런타임이 GraphNode UI를 실행하지 않는다.
- 선택지 순서와 텍스트가 저장/로드 왕복에서 보존된다.
- UI signal 연결 전 첫 요청 유실을 deferred start로 방지한다.
- 연속 대화와 이전 UI의 지연 종료 signal을 안전하게 처리한다.
- C#에서도 `/root/DialogueManager`를 통해 대화를 시작할 수 있다.

## Verification

- Godot 4.6.3 headless editor load 통과
- Choice 텍스트 load/capture round-trip 통과
- Step별 코드 리뷰 및 수정 후 재검증 완료

## Follow-ups

- [[DT-002-Portrait-State]]
- 통합 회귀 그래프와 자동 테스트
- Autoload/SceneFunction 정책

## Related

- [[DialogueTool-Step-1-to-8]]
- [[ADR-001-Runtime-Snapshot]]
- [[ADR-002-Editor-Adapter]]
- [[ADR-003-DialogueManager-Autoload]]

