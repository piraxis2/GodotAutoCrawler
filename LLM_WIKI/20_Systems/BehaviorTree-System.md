---
type: system
system: BehaviorTree
status: active
updated: 2026-06-11
---

# BehaviorTree System

## Agent Brief

- 주요 위치: `addons/behaviortree`, `Assets/Script/AutoCrawlerBehaviorTree`
- 책임: 자동 전투 유닛의 조건 평가와 행동 선택
- 상태: Failure, Success, Running
- 주의: 노드 상태 초기화, owner 타입, Tree/Blackboard 연결

## Main Nodes

- Selector: 성공 또는 실행 중인 첫 자식 반환
- Sequence: 실패 또는 실행 중인 첫 자식 반환
- RatingSelector: RatingDecorator 점수가 가장 높은 후보 실행
- Action: 이동 또는 TurnAction 선택
- Decorator: 상대 탐색, 복수 상대 조건 등

## Integration

`CharacterArticle`이 자신의 `BehaviorTree`를 실행한다. 행동 노드는 `BattleFieldScene`과 `BattleFieldTileMapLayer`를 통해 대상과 이동 경로를 찾는다.

## Verification

- Root가 없을 때 Failure인지 확인
- Running 노드가 다음 frame에 정상 재개되는지 확인
- 대상 사망/삭제 후 Blackboard 참조가 남지 않는지 확인

