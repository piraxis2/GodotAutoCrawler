---
id: ADR-006
type: decision
status: proposed
date: 2026-06-12
system: WorldState
---

# Typed World State

## Context

대규모 반응형 대화는 수백~수천 개의 quest, actor, faction, player 상태를 조회하고 변경한다. 자유 형식 Dictionary와 그래프 내부 Variable만 사용하면 오타, 타입 불일치, 세이브 호환 문제를 사전에 발견하기 어렵다.

목표 대화 모델에서는 상태가 단순 분기 하나를 고르는 데 그치지 않는다. 선택지 노출, NPC 응답 우선순위, 반복 대화, 과거 행동 기억, 여러 Effect의 원자 적용과 디버그 설명까지 같은 상태 계약을 사용해야 한다. 따라서 저장소 API뿐 아니라 이후 조건 평가와 mutation이 지켜야 할 경계도 이 결정에 포함한다.

## Decision

게임 상태를 `StateSchema`에 등록하고 `WorldStateStore`를 통해서만 접근한다.

- 모든 runtime key는 schema에 등록한다.
- 각 key는 타입, default, lifetime, 쓰기 가능 여부를 가진다.
- key namespace와 저장 lifetime을 분리한다.
- strict type validation을 적용한다.
- Store는 파일을 저장하지 않고 snapshot export/import만 제공한다.
- batch mutation은 전체 validation 후 atomic하게 적용한다.
- Dialogue runtime은 교체 가능한 state provider 경계를 사용한다.
- read provider와 mutation provider를 분리한다.
- 조건 평가는 pure read이며 mutation은 명시적 Effect 단계에서만 수행한다.
- snapshot은 JSON 호환 wire format을 사용하고 import는 replace-load로 동작한다.
- 잘못된 schema는 부분 등록하지 않고 전체 초기화를 실패시킨다.

## Alternatives

### Free-form Dictionary

간단하지만 오타와 타입 오류가 runtime까지 드러나지 않는다. 대규모 분기와 저장 마이그레이션에 부적합하다.

### Autoload Property Direct Access

Godot 객체를 바로 읽을 수 있으나 DialogueTool이 게임 씬 구조와 결합되고 테스트와 저장 정책이 분산된다.

### One Resource per Variable

에디터 참조는 강하지만 변수 수가 많아지면 파일과 리소스 관리 비용이 지나치게 커진다.

## Key Policy

- canonical key는 lower snake case dot path다.
- namespace 예: `quest`, `actor`, `faction`, `player`, `world`, `dialogue`
- dynamic key 생성은 금지한다.
- rename/migration은 별도 alias 정책 전까지 허용하지 않는다.

## Type Policy

초기 지원 타입은 bool, int, float, String, StringName이다. 암시적 변환을 허용하지 않는다. Object, Resource, Callable은 금지한다.

runtime에서는 String과 StringName을 별도 타입으로 유지한다. snapshot wire format에서는 StringName 값을 String으로 정규화하고 schema를 기준으로 복원한다.

## Lifetime Policy

- SAVE: save snapshot에 포함
- SESSION: 현재 실행에만 유지

Profile/account 범위는 실제 요구가 생길 때 추가한다. Dialogue-local 변수와 ConversationContext는 WorldStateStore에 넣지 않는다.

## Persistence Policy

WorldStateStore는 save slot과 file path를 알지 않는다. 외부 SaveGame 시스템이 snapshot을 직렬화한다.

snapshot은 schema version을 포함한다. import는 applied/ignored/errors report를 반환한다. key rename migration은 후속 ADR에서 다룬다.

import는 load 의미를 가진다. commit 전에 최상위 구조와 schema version을 검증하고, 성공하면 SAVE lifetime을 default로 초기화한 뒤 유효한 값만 적용한다. version 불일치는 전체 거부한다. unknown/SESSION/type mismatch 항목은 개별적으로 무시하고 report에 기록한다.

## Evaluation Policy

- ConditionEvaluator는 read provider만 의존한다.
- 한 평가 중에는 외부 mutation으로 관찰 값이 바뀌지 않도록 논리적인 state revision을 사용한다. revision API와 snapshot view 구현은 ConditionSet Task에서 확정한다.
- 조건 결과는 최종 bool뿐 아니라 key, operator, expected, actual과 실패 위치를 trace할 수 있어야 한다.
- 임의 함수 호출이나 부작용이 있는 Godot Expression을 상태 조건의 표준 형식으로 사용하지 않는다.
- Response Selector의 우선순위와 tie-break는 데이터에 명시하고 결정론적으로 평가한다.

## Mutation Policy

- gameplay mutation은 단일 set 또는 atomic batch로만 수행한다.
- batch의 모든 변경은 commit 전에 검증한다.
- 동일 key 중복은 전체 batch 오류다.
- commit 완료 후 변경 signal을 결정된 순서로 발행한다.
- mutation 결과는 향후 DialogueHistory와 디버거가 기록할 수 있는 diff를 반환한다.

## Scale Policy

초기 구현은 단일 StateSchema resource를 사용하지만 key/API는 여러 schema fragment를 하나의 registry로 합치는 후속 확장을 허용한다. fragment override, mod load order, hot reload는 현재 범위가 아니다. 실제 규모에서 authoring과 로드 성능을 측정하기 전 복잡한 registry 계층을 미리 구현하지 않는다.

## Consequences

### Positive

- key 오타와 타입 오류를 조기에 검출한다.
- Dialogue, quest, gameplay가 동일한 상태 계약을 공유한다.
- 조건 평가와 Effect 적용을 설명 가능하게 추적할 수 있다.
- 저장 시스템과 Dialogue runtime의 결합을 줄인다.
- fake provider를 사용한 테스트가 가능하다.
- 조건 실패와 상태 변경을 설명 가능한 trace/diff로 발전시킬 수 있다.
- 대화 그래프와 save format이 직접 결합되지 않는다.

### Negative

- 변수 추가 시 schema를 먼저 수정해야 한다.
- schema version과 migration을 장기적으로 관리해야 한다.
- 동적 actor/quest instance 상태에는 후속 설계가 필요하다.
- 초기 구현량이 자유 Dictionary보다 크다.
- 조건 평가 중 revision 안정성과 대규모 schema authoring 도구가 후속 과제로 남는다.

## Follow-ups

- schema migration/key alias ADR
- ConditionSet과 ConditionEvaluator
- State mutation Effect command
- State Inspector 및 evaluation trace
- DialogueHistory와 ConversationContext의 소유 경계

## Related

- [[DT-005-StateSchema-WorldStateStore]]
- [[World-State-System]]
