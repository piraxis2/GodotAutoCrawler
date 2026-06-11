---
type: task-index
project: AutoCrawler
updated: 2026-06-11
---

# Open Tasks

## Next

- [[DT-002-Portrait-State]]: Portrait를 Say와 느슨하게 결합된 UI 상태 명령으로 설계한다.
- Dialogue 통합 회귀 그래프 작성: Start, Say, Choice, Expression, Branch, End를 한 리소스에서 검증한다.
- DialogueManager 반복 실행/교체/연속 실행 테스트를 자동화한다.

## Later

- Set Variable 노드
- Compare 노드
- Random Branch 노드
- Narration 노드
- Emit Event 노드
- Wait 및 Sound 연출 노드
- Entry Point 또는 named entry 지원

## Deferred Architecture

- Definition의 Adapter 호출 중계 제거
- NodeTypeRegistry 기반 에디터 노드 팩토리화
- Autoload read와 write/effect 노드의 책임 분리
- SceneFunction 호출 대상, 인자, 반환값, 실패 정책 확정

## Maintenance

- 시스템 문서는 코드 변경 후 현재 사실만 남도록 갱신한다.
- 완료 작업은 Task 문서에 검증 결과를 남기고 이 목록에서 제거한다.
- 새로운 중요한 설계 선택은 ADR을 먼저 작성한다.

