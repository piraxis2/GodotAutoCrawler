![Image](https://github.com/user-attachments/assets/703b98a0-920b-4957-86b8-05c38c8b34c0)
![Image](https://github.com/user-attachments/assets/1f17f8ca-3d86-44aa-984c-e8cb5cfd2f68)
# AutoCrawler

AutoCrawler는 완전히 자동으로 작동하는 텍틱컬 RPG 구현을 위한 기반 시스템입니다. 
다양한 상태 효과, AI 행동 트리, 전투 유닛, 턴 관리 시스템 등을 포함하고 있습니다. 
 
---

## 주요 요소

### 1. **StatusAffect**
`StatusAffect`는 게임 내 상태 효과를 정의하고 관리하는 추상 클래스입니다. 이 클래스는 특정 상태 효과를 대상(`ArticleStatus`)에 적용하거나 제거하는 역할을 합니다.

#### 주요 프로퍼티와 메서드:
- **`AffectedType`**: 상태 효과가 영향을 미치는 상태 요소(`StatusElement`)의 타입을 정의합니다.
- **`Cost`**: 상태 효과가 유지되는 동안 소모되는 코스트를 계산합니다.
- **`Apply`**: 상태 효과를 대상에게 적용합니다.
- **`AffectedEnd`**: 상태 효과가 종료될 때 호출되며, 관련된 상태를 정리합니다.
- **`OnAffectedEnd`**: 상태 효과 종료 시 발생하는 이벤트입니다.

#### 관련 인터페이스:
- **`IAffectedImmediately`**: 즉시 적용되는 상태 효과를 정의합니다.
- **`IAffectedOnlyMyTurn`**: 자신의 턴에만 적용되는 상태 효과를 정의합니다.
- **`IAffectedUntilTheEnd`**: 특정 조건이 충족될 때까지 지속되는 상태 효과를 정의합니다.

#### 동작 예시:
`StatusAffect`는 `ArticleStatus`와 연동되어 상태 효과를 적용하거나 제거합니다. 예를 들어, 특정 상태 효과가 즉시 적용되는 경우 `IAffectedImmediately` 인터페이스를 통해 처리됩니다.

---

### 2. **BehaviorTree**
`BehaviorTree`는 AI의 행동을 제어하는 데 사용되는 트리 구조입니다. 각 노드는 특정 행동 또는 조건을 나타내며, 이를 통해 복잡한 AI 행동을 간단하게 구현할 수 있습니다.

#### 주요 구성 요소:
- **`BehaviorTree`**: 트리의 루트로, AI의 전체 행동 흐름을 제어합니다. 트리의 최상위 노드이며, 자식 노드들을 통해 행동을 실행합니다.
- **`BehaviorTree_Node`**: 트리의 기본 구성 요소로, 행동(액션), 조건(데코레이터), 또는 흐름 제어(컴포지트)를 나타냅니다.
- **`BehaviorTree_Action`**: 특정 행동을 수행하는 노드입니다. 예를 들어, 이동, 공격, 대기 등의 행동을 정의합니다.
- **`BehaviorTree_Composite`**: 여러 자식 노드를 포함하며, 자식 노드의 실행 순서를 제어합니다. 대표적인 유형은 다음과 같습니다:
  - **`BehaviorTree_Selector`**: 자식 노드 중 하나가 성공하면 실행을 종료합니다. 실패한 경우 다음 자식 노드를 실행합니다.
  - **`BehaviorTree_Sequence`**: 모든 자식 노드가 순서대로 성공해야 실행을 종료합니다. 하나라도 실패하면 실행을 중단합니다.
  - **`BehaviorTree_RatingSelector`**: 자식 노드 중 Rating이 가장 높은 노드를 선택하여 실행합니다.
- **`BehaviorTree_Decorator`**: 단일 자식 노드를 감싸며, 조건을 추가하거나 실행 결과를 수정합니다. 예를 들어, 특정 조건이 충족될 때만 실행되도록 설정할 수 있습니다.


#### 동작 예시:
1. **Selector**:
   - 자식 노드 중 하나가 성공하면 나머지 노드는 실행되지 않습니다.
   - 예: "적이 근처에 있는가?" → "공격" 또는 "대기".

2. **Sequence**:
   - 모든 자식 노드가 순서대로 성공해야 전체가 성공으로 간주됩니다.
   - 예: "목표 위치로 이동" → "공격 준비" → "공격".

3. **Decorator**:
   - 특정 조건이 충족될 때만 자식 노드를 실행합니다.
   - 예: "체력이 50% 이상인가?" → "공격".

#### BehaviorTree의 역할:
- **AI 의사결정**: 트리를 통해 AI가 상황에 따라 적절한 행동을 선택합니다.
- **유연성**: 트리 구조를 변경하거나 노드를 추가하여 AI의 행동 패턴을 쉽게 확장할 수 있습니다.
- **재사용성**: 공통적인 행동 패턴을 노드로 정의하여 여러 AI에 재사용할 수 있습니다.

---

### 3. **Article**
`Article`은 게임 내에서 상호작용 가능한 모든 객체를 나타내는 기본 클래스입니다. 캐릭터, 장애물, 아이템 등이 모두 `Article`의 서브클래스로 구현됩니다.

#### 주요 클래스:
- **`ArticleBase`**: 모든 `Article`의 기본 클래스.
- **`ArticleStatus`**: 각 `Article`의 상태를 관리하며, `StatusElement`와 `StatusAffect`를 통해 상태를 제어합니다.

#### 주요 기능:
- **상태 관리**: `ArticleStatus`를 통해 상태 요소와 상태 효과를 관리합니다.
- **위치 및 이동**: `BattleFieldTileMapLayer`와 연동되어 전투 필드에서의 위치를 관리합니다.
- **이벤트 처리**: `on_dead`와 같은 이벤트를 통해 객체의 생명주기를 관리합니다.

#### ArticleStatus의 동작:
- **`InitStatus`**: `Article`의 상태를 초기화합니다.
- **`ApplyAffectStatus`**: 상태 효과를 적용합니다.
- **`RemoveAffectStatus`**: 상태 효과를 제거합니다.
- **`ApplyAffectingStatuses`**: 현재 적용 중인 상태 효과를 반복적으로 적용합니다.

---

### 4. **TurnHelper**
`TurnHelper`는 턴 기반 시스템을 관리하는 핵심 클래스입니다. 각 턴의 순서를 결정하고, 턴이 진행되는 동안 필요한 작업을 수행합니다.

#### 주요 기능:
- **턴 순서 관리**: 각 `Article`의 우선순위(`Priority`)를 기반으로 턴 순서를 결정합니다.
- **턴 진행**: 현재 턴의 `Article`이 행동을 완료하면 다음 턴으로 넘어갑니다.
- **행동 제어**: `TurnActionBase`를 통해 각 턴에서 수행할 행동을 정의합니다.

#### 관련 클래스:
- **`TurnActionBase`**: 턴에서 수행할 행동의 기본 클래스.
- **`ITurnAffectedArticle`**: 턴 기반 행동을 수행하는 객체를 위한 인터페이스.

#### ITurnAffectedArticle의 동작:
- **`Priority`**: 턴 순서를 결정하는 우선순위입니다.
- **`CurrentTurnAction`**: 현재 턴에서 수행할 행동을 나타냅니다.
- **`TurnPlay`**: 턴 진행 중 AI의 행동을 정의합니다.

---

# 개선 목표 

## Behavior Tree Editor
- 실시간 상태 추적: Behavior Tree의 각 노드 상태(성공, 실패, 실행 중)를 실시간으로 시각화하여 디버깅 효율성을 높일 예정입니다.
- 노드 실행 기록: 각 노드의 실행 이력을 기록하고, 이를 기반으로 AI의 의사결정 과정을 분석할 수 있는 기능을 추가할 계획입니다.
- Breakpoints 지원: 특정 노드에서 실행을 중단하고 상태를 점검할 수 있는 Breakpoints 기능을 구현할 예정입니다.
- 테스트 환경 통합: Behavior Tree를 독립적으로 테스트할 수 있는 환경을 제공하여, 게임 실행 없이도 AI의 동작을 검증할 수 있도록 할 예정입니다.

# 각종 스킬
각종 스킬을 구현할 예정입니다. 

# 웹 포팅
웹으로 포팅하여 가벼운 승부 게임, 요컨대 사다리타기 같은 복불복 게임으로 공개하려 합니다. 
그러나 [.NET WebBuild 이슈](https://github.com/godotengine/godot/issues/70796) 로 인하여 Godot 4 .NET은 현재 웹빌드가 불가능 하여 gds로 수정하거나 해당 이슈가 해결되길 주시하며 기다려야 할 것으로 보입니다.  🥹
