# AutoCrawler 상세 기술 문서

## 1. 개요

AutoCrawler는 Godot 엔진과 C#을 사용하여 개발된 **완전 자동 턴제 택티컬 RPG**의 기반 시스템입니다. 이 문서는 프로젝트의 핵심 아키텍처, 주요 클래스의 역할 및 상호작용을 코드 수준에서 상세히 설명합니다.

---

## 2. Article: 게임 세계의 모든 객체

`Article`은 게임 내에 존재하는 모든 상호작용 가능한 객체(캐릭터, 장애물, 아이템 등)의 최상위 추상 클래스입니다.

### `ArticleBase.cs`
모든 Article 객체의 기반이 되는 클래스입니다. `Node2D`를 상속받아 Godot 씬 트리에 배치될 수 있으며, 공통적인 기능(위치, 생존 여부, 애니메이션)을 제공합니다.

#### 주요 프로퍼티 및 메서드:
-   `ArticleStatus`: 모든 상태 정보(체력, 스탯 등)를 관리하는 객체입니다. (아래 `ArticleStatus` 항목 참조)
-   `IsAlive`: 현재 생존 여부를 `Health` 상태를 통해 확인합니다.
-   `TilePosition`: 타일맵에서의 그리드 좌표입니다. 이 값이 변경되면 `OnMove` 시그널이 발생합니다.
-   `OnDead` / `OnMove`: 각각 Article의 죽음과 이동 시 발생하는 시그널입니다.
-   `IsOpponent()`: 대상 Article이 적인지 판별합니다.

```csharp
// In ArticleBase.cs
public abstract partial class ArticleBase : Node2D
{
    [Export] public ArticleStatus ArticleStatus = new();

    [Signal]
    public delegate void OnMoveEventHandler(Vector2I from, Vector2I to, ArticleBase article);

    [Signal]
    public delegate void OnDeadEventHandler(ArticleBase deadArticle);

    // ...
}
```

---

## 3. Status: 상태 관리 시스템

Article의 모든 동적인 상태(능력치, 버프, 디버프)를 관리하는 시스템입니다. `ArticleStatus`를 중심으로 `StatusElement`와 `StatusAffect`가 유기적으로 동작합니다.

### `ArticleStatus.cs`
`ArticleBase`에 소속되어 해당 Article의 모든 상태를 총괄합니다. `StatusElement`(고정 스탯)의 딕셔너리와 `StatusAffect`(지속 효과)의 리스트를 가집니다.

```csharp
// In ArticleStatus.cs
public partial class ArticleStatus : Resource
{
    // ...
    // 상태 효과를 리스트에 추가하고, 즉시 발동 타입이면 즉시 적용
    public void ApplyAffectStatus(StatusAffect statusAffect)
    {
        if (statusAffect is not IAffectedImmediately)
        {
            statusAffect.OnAffectedEnd += () => RemoveAffectStatus(statusAffect);
            AffectingStatusesList.Add(statusAffect);
        }
        if (statusAffect is IAffectedImmediately)
        {
            statusAffect.Apply(this);
        }
    }

    // 매 턴(또는 필요한 시점)에 호출되어 지속 효과들을 적용
    public void ApplyAffectingStatuses()
    {
        foreach (var affectStatus in AffectingStatusesList)
        {
            affectStatus.Apply(this);
        }
    }
}
```

### `StatusAffect.cs`
버프, 디버프, 도트 데미지와 같은 '상태 효과'를 정의하는 추상 클래스입니다. `Cost`를 통해 지속 시간을 관리하며, 특정 `StatusElement`에 영향을 줍니다.

-   **`AffectedType`**: 이 효과가 어떤 `StatusElement`(예: `Health`, `Power`)에 영향을 주는지 정의합니다.
-   **`Apply()`**: `ArticleStatus`에 의해 호출되어 실제 효과를 적용합니다.
-   **`Cost`**: 상태 효과의 지속 시간. `Apply`가 호출될 때마다 1씩 감소하며 0이 되면 효과가 종료됩니다.

---

## 4. BehaviorTree: 자율 행동 AI

캐릭터의 의사결정 로직을 트리 구조로 설계합니다. 각 노드는 특정 행동이나 조건을 나타냅니다.

### 주요 Composite 노드
-   **`BehaviorTree_Selector`**: 자식 노드 중 하나라도 **성공 또는 실행 중** 상태가 되면 즉시 해당 상태를 반환합니다. (OR 연산)
-   **`BehaviorTree_Sequence`**: 자식 노드 중 하나라도 **실패 또는 실행 중** 상태가 되면 즉시 해당 상태를 반환합니다. (AND 연산)
-   **`BehaviorTree_RatingSelector`**: 자식 노드 중 `BehaviorTree_RatingDecorator`들을 평가하여 `GetRating()` 점수가 가장 높은 노드 하나만을 선택하여 실행합니다. 이를 통해 여러 행동 후보 중 가장 가치 있는 행동을 동적으로 결정할 수 있습니다. (예: "체력이 낮은 적 공격" vs "버프 사용")

    ```csharp
    // In BehaviorTree_RatingSelector.cs
    protected override BtStatus OnBehave(double delta, Node owner)
    {
        // ...
        // 가장 높은 점수의 Decorator를 찾는다
        _highestRatingDecorator = TakeDecorator(owner);
        // ...
        // 해당 Decorator만 실행한다
        var status = _highestRatingDecorator.Behave(delta, owner);
        // ...
    }
    ```

---

## 5. TurnHelper: 턴 관리 시스템

게임의 턴 순서와 진행을 관리하는 핵심 싱글톤 클래스입니다.
-   `_PhysicsProcess`에서 현재 턴을 가진 Article(`_currentTurnArticle`)의 `TurnPlay()` 메서드를 매 프레임 호출합니다.
-   `TurnPlay()`의 결과가 `Success` 또는 `Failure`이면, 턴이 종료된 것으로 간주하고 다음 Article로 턴을 넘깁니다.

```csharp
// In TurnHelper.cs
public partial class TurnHelper : Node
{
    private readonly List<ITurnAffectedArticle<ArticleBase>> _turnAffectedArticleList = new();
    private ITurnAffectedArticle<ArticleBase> _currentTurnArticle;

    public override void _PhysicsProcess(double delta)
    {
        // ...
        BtStatus status = _currentTurnArticle.TurnPlay(delta * Speed);

        if (status is BtStatus.Success or BtStatus.Failure)
        {
            _currentTurnArticle = GetNextTurnArticle();
        }
    }
    //...
}
```

---
## 6. 핵심 관리자 및 컨테이너

### `ArticlesContainer.cs`
씬에 배치된 모든 `ArticleBase` 객체들을 "Ally", "Opponent", "Neutral" 그룹으로 나누어 관리하는 컨테이너입니다. `TurnHelper`나 AI가 적군 리스트를 참조하는 등, 씬 전체의 유닛 정보에 접근할 때 사용됩니다.

```csharp
// In ArticlesContainer.cs
public partial class ArticlesContainer : Node
{
    public Dictionary<string, List<ArticleBase>> Articles { get; } = new();

    public override void _Ready()
    {
        // 씬 트리를 순회하며 자식 Article들을 그룹에 맞게 추가
        foreach (ArticleBase article in GetChildren().SelectMany(child => child.GetChildren().OfType<ArticleBase>()))
        {
            Articles[article.GetParent().Name].Add(article);
        }
    }
}
```

### `CheatManager.cs`
`devconsole` 애드온과 연동하여 디버깅용 치트키나 명령어를 관리하는 클래스입니다. 향후 이곳에 `[Command]` 어트리뷰트를 단 메서드를 추가하여 콘솔 명령어를 확장할 수 있습니다.

---

## 7. 커스텀 애드온 (Custom Addons)

이 프로젝트를 위해 직접 제작된 Godot 에디터 플러그인들입니다.

### `DevConsole`
-   **설명**: `F1` 키를 눌러 런타임에 커맨드를 입력할 수 있는 개발자 콘솔 창을 제공합니다.
-   **구조**: `devConsole.gd`가 플러그인 진입점이며, UI는 `consoleWindow.tscn` 씬으로 구성됩니다. `CheatManager.cs`에 정의된 함수들을 리플렉션으로 찾아 커맨드로 등록하고 실행하는 구조를 가집니다.

### `DialogueTool`
-   **설명**: 노드 기반의 그래프 에디터를 통해 게임 내 대화나 이벤트 흐름을 시각적으로 제작할 수 있는 툴입니다.
-   **구조**: `dialoguetool_main.gd`가 메인 UI 로직을 담당하며, `Editor/editor.gd`가 `GraphEdit` 노드의 핵심 기능을 구현합니다. 제작된 다이얼로그는 리소스 파일로 저장되어 런타임에 `Autoload` 노드를 통해 실행됩니다.

---

## 8. 시스템 상호작용 흐름 (요약)

1.  **게임 시작**: `ArticlesContainer`가 씬의 모든 유닛을 수집하고 그룹화합니다.
2.  **턴 시작**: `TurnHelper`가 우선순위에 따라 현재 턴의 `_currentTurnArticle`을 결정합니다.
3.  **행동 결정**: `TurnHelper`가 `_currentTurnArticle.TurnPlay()`를 호출합니다. 이 메서드는 내부적으로 해당 Article의 **BehaviorTree**를 실행합니다.
4.  **AI 연산**: BehaviorTree는 `Selector`, `Sequence`, `RatingSelector` 등을 통해 현재 상황에 가장 적합한 행동(예: '적을 향해 이동')을 결정합니다.
5.  **행동 수행**: 결정된 행동(Action 노드)이 실행됩니다. 이 과정에서 `ArticleBase.TilePosition`이 변경되거나 `FxPlayer`가 호출될 수 있습니다.
6.  **상태 변화**: 행동의 결과로 특정 대상에게 `ArticleStatus.ApplyAffectStatus()`가 호출되어 새로운 상태 효과(`StatusAffect`)가 적용될 수 있습니다.
7.  **턴 종료**: BehaviorTree의 실행이 끝나 `Success` 또는 `Failure`를 반환하면, `TurnHelper`는 다음 순서의 Article로 턴을 넘깁니다.
8.  **반복**: 2-7 과정이 게임 종료 조건이 충족될 때까지 반복됩니다.
