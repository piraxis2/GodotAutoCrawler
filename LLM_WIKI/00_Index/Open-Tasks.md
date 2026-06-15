---
type: task-index
project: AutoCrawler
updated: 2026-06-14
---

# Open Tasks

## Next

- SaveGame file/slot system: DT-006 snapshot adapter(`capture_world_state`/`restore_world_state`)를
  소비해 실제 파일, 슬롯, 백업 정책을 구현한다(DT-006 후속, [[DT-006-WorldState-Runtime-Review]]).
- State Read / Set·Add State Effect Dialogue 노드: DT-005 provider/`apply_batch`를 실제 소비하는 노드.
- Say 줄 누적 표시 실제 UI 회귀 검증: 한 줄/여러 줄/빈 줄/CRLF의 클릭 순서와 Flow 진행을 Godot에서 확인한다.
- Dialogue 통합 회귀 그래프 작성: Start, Say, Choice, Expression, Branch, End를 한 리소스에서 검증한다.
- DialogueManager 반복 실행/교체/연속 실행 테스트를 자동화한다.

## Later

- schema-aware key/operator picker와 inline ConditionSet tree editor — DT-008 후속(현재는 외부
  `.tres` ConditionSet 지정 중심, runtime provider 검증만).
- 조건 평가 trace inspector UI와 disabled-choice + reason UI — DT-008 `condition_evaluated` seam 소비.
- Response Selector와 weighted/random response
- DialogueHistory 및 State Inspector
- Portrait transition 애니메이션(fade/slide 등) — DT-002 MVP 이후.
- Portrait Focus와 비활성 Portrait dim 처리 — DT-002 MVP 이후.
- actor database 및 actor/expression -> Texture resolver — DT-002 MVP 이후.
- speaker 기반 자동 Portrait focus/선택 정책 — DT-002 MVP 이후.
- 기존 `SayDef.portrait` 데이터의 명시적 마이그레이션 도구.
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
