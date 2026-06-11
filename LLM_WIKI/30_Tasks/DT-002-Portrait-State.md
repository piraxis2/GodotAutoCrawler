---
id: DT-002
type: task
status: proposed
system: DialogueTool
created: 2026-06-11
tags: [task, dialogue-tool, portrait]
---

# Portrait State

## Goal

Portrait 연출을 Say에 강제 결합하지 않고, 대화 UI의 느슨한 지속 상태로 제공한다.

## Proposed Model

```gdscript
{
    "left": {
        "actor": "noabel",
        "expression": "normal",
        "visible": true
    },
    "right": {
        "actor": "princess",
        "expression": "angry",
        "visible": true
    }
}
```

## Candidate Nodes

- Portrait Show: slot, actor, expression, transition
- Portrait Hide: slot 또는 actor, transition
- Portrait Expression: 기존 slot의 expression 변경
- Portrait Focus: 다른 Portrait dim 또는 현재 발화자 강조. 후순위.

## Design Constraints

- Say는 Portrait가 없어도 실행돼야 한다.
- Portrait 명령은 UI state 변경 요청이며 Flow 실행 규칙과 분리한다.
- Actor database는 MVP 이후 도입한다.
- 리소스 path 문자열을 직접 저장할지 actor id를 저장할지 ADR이 필요하다.

## Suggested Steps

- [ ] Portrait runtime request와 상태 모델 정의
- [ ] DialogueUI에 left/center/right slot 추가
- [ ] Show/Hide/Expression 노드 및 Adapter 구현
- [ ] 저장/재로드 round-trip 검증
- [ ] 연속 Say에서 Portrait 상태 유지 검증
- [ ] 대화 종료/교체 시 상태 정리 검증

## Completion Criteria

- Portrait 없이 기존 대화가 동일하게 동작한다.
- Portrait 상태가 다음 Say까지 유지된다.
- Hide와 Expression 변경이 slot 단위로 동작한다.
- 대화 교체 시 이전 Portrait 상태가 남지 않는다.

