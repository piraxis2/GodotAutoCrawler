# Implementation Prompt

아래 프롬프트는 설계 리뷰에서 승인된 Task의 Step 하나를 구현할 때 사용한다.

```text
AGENTS.md와 LLM_WIKI를 먼저 읽고 실제 코드와 대조해라.

이번 세션은 구현 전용이다. 승인된 설계 범위를 임의로 확장하지 마라.

구현 대상:
- Task: [Task 문서 경로]
- ADR: [승인된 ADR 문서 경로]
- Step: [Step 번호와 이름]
- 관련 설계 리뷰: [리뷰 결과 또는 링크]

먼저 확인할 것:
1. Task와 ADR의 status 및 승인된 결정
2. 해당 Step의 선행 Step 완료 여부
3. git status와 사용자 변경
4. Wiki와 실제 코드의 불일치

구현 규칙:
- 이번 Step의 Scope와 Completion Criteria만 구현한다.
- Out of Scope 항목은 구현하지 않는다.
- 승인된 API, 데이터 모델, 오류 정책을 따른다.
- 설계 변경이 필요하면 임의로 결정하지 말고 구현을 멈춘 뒤 Design Deviation으로 보고한다.
- 기존 사용자 변경을 되돌리지 않는다.
- 관련 없는 리팩터링과 메타데이터 변경을 피한다.
- 테스트가 가능한 구조로 구현하고 위험도에 맞는 검증을 추가한다.

검증:
- Step의 정상 흐름과 실패 흐름
- 저장/재로드 또는 snapshot 왕복
- 반복 실행, 재진입, 객체 수명 주기
- 기존 회귀 테스트
- Godot headless editor load

완료 보고 형식:

## Implemented
- 변경한 동작
- 변경 파일

## Design Compliance
- Task/ADR 요구사항별 대응
- 설계에서 벗어난 부분이 있다면 이유

## Verification
- 실행한 명령과 테스트
- 결과
- 실행하지 못한 검증

## Remaining Risk
- 테스트 공백
- 다음 Step으로 넘긴 항목

## Wiki Updates
- 갱신한 Task/System/Current-State 문서

구현 완료 후 스스로 최종 승인하지 마라. 별도의 코드 리뷰를 요청할 수 있도록 결과를 남겨라.
```

## Design Deviation Rule

구현 중 다음 상황이 나오면 설계로 되돌아간다.

- 승인된 public API로 완료 조건을 달성할 수 없음
- 데이터 손실 또는 저장 호환 문제가 새로 발견됨
- 기존 시스템의 책임과 중복됨
- Step 범위를 넘어서는 선행 구현이 필요함
- ADR의 핵심 결정 변경이 필요함

이때 제품 코드를 억지로 확장하지 말고 다음을 보고한다.

```markdown
## Design Deviation
- 발견한 제약
- 영향을 받는 Task/ADR
- 가능한 선택지
- 권장 설계 변경
- 현재까지 안전하게 완료된 작업
```

