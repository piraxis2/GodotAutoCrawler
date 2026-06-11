---
type: system
system: DialogueTool
status: active
updated: 2026-06-11
---

# DialogueTool

## Responsibilities

- GraphEdit 기반 대화 작성
- `.tres` 저장 및 재로드
- Flow/Data 포트 validation
- 런타임 대화 실행
- 텍스트와 선택지 UI
- 현재 실행 노드 디버그 하이라이트
- 게임 코드용 전역 실행 API

## Supported Runtime Nodes

| Type | Role |
| --- | --- |
| `start` | 시작 후 첫 Flow로 자동 이동 |
| `say` | 텍스트 UI 요청 후 advance 대기 |
| `choice` | 선택지 UI 요청 후 index 대기 |
| `branch` | Data 입력을 bool로 평가해 분기 |
| `end` | 대화 종료 |
| `variable` | 정적 값 또는 random range 제공 |
| `expression` | 연결된 Data 입력으로 Expression 평가 |

## Editor-Only or Incomplete Nodes

- Description: 그래프 메모와 시각 구분
- Test: 에디터 실험용
- Autoload: 정의와 UI는 있으나 런타임 정책 미완성
- SceneFunction: 정의와 UI는 있으나 실행 정책 미완성

## Validation

- Start 노드는 정확히 하나여야 한다.
- 연결 endpoint가 존재해야 한다.
- Flow와 Data 포트를 섞은 연결은 저장을 중단한다.
- 도달 불가능한 Flow와 Start의 연결 누락은 warning이다.

## Extension Rules

새 노드는 최소한 다음을 정의한다.

1. runtime type
2. 직렬화 가능한 runtime params
3. 에디터 Adapter 또는 고정 `.tscn` UI
4. 포트 계약
5. DialoguePlayer 실행 또는 data 평가 규칙
6. 저장/재로드 왕복 검증

## Related

- [[DialogueTool-Architecture]]
- [[DT-002-Portrait-State]]
- [[DialogueTool-Step-1-to-8]]

