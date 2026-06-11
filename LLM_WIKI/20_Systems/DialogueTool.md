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
| `portrait_show` | Portrait 표시 요청(비대기) 발행 후 다음 Flow로 진행 |
| `portrait_hide` | Portrait 숨김 요청(비대기) 발행 후 다음 Flow로 진행 |
| `portrait_expression` | Portrait 표정 변경 요청(비대기) 발행 후 다음 Flow로 진행 |
| `end` | 대화 종료 |
| `variable` | 정적 값 또는 random range 제공 |
| `expression` | 연결된 Data 입력으로 Expression 평가 |

## Portrait Nodes (DT-002 MVP)

- `portrait_show`/`portrait_hide`/`portrait_expression`은 Say와 독립된 비대기 Flow 명령이다.
- 에디터에서 생성·편집·저장/재로드된다. Definition은 공통 베이스 `PortraitDef`(Abstract/)를
  상속하고 에디터 UI는 공통 `portrait_editor_adapter`에 위임한다.
- DialoguePlayer가 비대기 `portrait_state` 요청으로 정규화해 발행하고, DialogueUI가
  left/center/right 슬롯에서 상태를 소유하며 `texture_path` Texture를 렌더링한다.
- 상태는 Say/Choice 전환에도 유지되고 대화 종료/교체 시 정리된다.
- Portrait Show/Expression의 `texture_path` 필드는 문자열 직접 입력과 FileSystem의
  Texture2D 리소스 드롭을 지원한다. 유효한 단일 Texture만 `res://` 경로로 반영한다.
- MVP 이후: transition 애니메이션, Portrait Focus/dim, actor/expression resolver. [[DT-002-Portrait-State]]

## Say Line Paging

- Say 텍스트의 줄바꿈은 같은 Say 노드 안의 페이지 경계로 처리한다.
- 첫 줄부터 타이핑하고 이전 줄은 같은 대화창에 유지한다. 클릭 시 현재 줄 완성 -> 다음 줄 누적 표시
  -> 마지막 줄 이후 다음 Flow 순서로 진행한다.
- 줄바꿈이 없는 기존 Say는 기존과 동일하게 현재 문장 완성 후 다음 클릭에 Flow를 진행한다.

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
