# 프로젝트 설명

이 프로젝트는 Godot .Net과 GDscript로 만들어 졌습니다.

완전히 자동으로 작동하는 텍틱컬 RPG 구현을 위한 기반 시스템입니다.

이 프로젝트는 `StatusAffect`와 `BehaviorTree`를 사용하여 게임 내 캐릭터의 행동과 상태를 관리합니다. `StatusAffect`는 캐릭터의 상태에 영향을 주는 요소를 정의하고, `BehaviorTree`는 캐릭터의 행동을 결정하는 트리 구조를 제공합니다.

## StatusAffect

각종 데미지 처리, Dot 데미지 처리, 버프, 디버프 등의 캐릭터 스테이터스에 영향을 미치는 시스템을 구현하기 위한 기반 클래스 입니다.

`StatusAffect`는 캐릭터의 상태에 영향을 주는 요소를 정의하는 추상 클래스입니다. 이 클래스는 다양한 상태 영향을 정의하는 데 사용됩니다.


### 주요 메서드

- `Apply(ArticleStatus recipient)`: 상태 영향을 적용합니다.
- `Unapply(ArticleStatus recipient)`: 상태 영향을 제거합니다.
- `EndAffect()`: 상태 영향이 끝났을 때 호출됩니다.

### 인터페이스

- `IAffectedImmediately`: 즉시 영향을 주는 상태를 정의합니다.
- `IAffectedOnlyMyTurn`: 자신의 턴에만 영향을 주는 상태를 정의합니다.
- `IAffectedUntilTheEnd`: 특정 조건이 끝날 때까지 영향을 주는 상태를 정의합니다.

## BehaviorTree

`BehaviorTree`는 캐릭터의 행동을 결정하는 트리 구조를 제공합니다. 각 노드는 특정 행동을 정의하며, 트리는 이러한 노드들을 계층적으로 구성합니다.

### 주요 클래스

- `BehaviorTree_Node`: 행동 트리의 기본 노드 클래스입니다. 모든 행동 트리 노드는 이 클래스를 상속받아야 합니다.
- `BehaviorTree_Action`: 행동을 수행하는 노드입니다. PerformAction 메서드를 구현하여 구체적인 행동을 정의합니다.
- `BehaviorTree_Composite`: 자식 노드를 가질 수 있는 복합 노드입니다. TreeChildren 속성을 통해 자식 노드를 관리합니다.
- `BehaviorTree_Decorator`: 자식 노드의 행동을 수정하거나 조건을 추가하는 데코레이터 노드입니다. Decorate 메서드를 구현하여 구체적인 데코레이터 동작을 정의합니다.
- `BehaviorTree_RatingDecorator`: 특정 조건에 따라 노드의 우선순위를 결정하는 데코레이터입니다. GetRating 메서드를 구현하여 우선순위를 계산합니다.
- `BehaviorTree_RatingSelector`: 여러 데코레이터 중에서 가장 높은 우선순위를 가진 데코레이터를 선택하여 실행하는 선택자 노드입니다.
- `BehaviorTree_Sequence`: 자식 노드를 순차적으로 실행하는 시퀀스 노드입니다. 모든 자식 노드가 성공해야만 성공 상태를 반환합니다.
- `BehaviorTree_Selector`: 자식 노드를 순차적으로 실행하여 첫 번째 성공 상태를 반환하는 선택자 노드입니다.

### 주요 메서드

- `Behave(double delta, Node owner)`: 노드의 행동을 실행합니다.
- `OnBehave(double delta, Node owner)`: 노드의 구체적인 행동을 정의합니다.
- `OnTreeChanged()`: 트리 구조가 변경되었을 때 호출됩니다.

## 예제

### StatusAffect 예제

```csharp
public class PhysicalDamage : Damage
{
    private int _minDamage;
    private int _maxDamage;
    private int _strength = 1;
    private bool _isCritical;

    protected override bool IsCritical => _isCritical;

    protected override void Init(ArticleStatus giver, int minDamage, int maxDamage)
    {
        _minDamage = minDamage;
        _maxDamage = maxDamage;
        _strength = (giver.StatusElementsDictionary[typeof(Strength)] as Strength)?.Value ?? _strength;
        int luckValue = (giver.StatusElementsDictionary[typeof(Luck)] as Luck)?.Value ?? 0;
        _isCritical = GD.RandRange(0, 100) < (luckValue / 10) + 5;
    }

    protected override int CalculatedDamage(ArticleStatus recipient)
    {
        int defenseValue = (recipient.StatusElementsDictionary[typeof(Defense)] as Defense)?.Value ?? 0;
        int calculatedMinDamage = (_minDamage - defenseValue) / 2 + _strength + 25;
        int calculatedMaxDamage = (_maxDamage - defenseValue) / 2 + _strength + 25;
        return GD.RandRange(calculatedMinDamage, calculatedMaxDamage);
    }
}
```

### BehaviorTree 예제

```csharp
public partial class BehaviorTree_Move : BehaviorTree_Action
{
    private AStarGrid2D _aStar2D;
    private Vector2I? _targetPosition;
    private Tween _moveTween;
    private double _elapsedTime;

    protected override void OnInit(Node owner) { }

    private Vector2I? FindTarget(ArticleBase owner, BattleFieldTileMapLayer tileMapLayer)
    {
        tileMapLayer.UpdateAStar(ref _aStar2D);

        if (owner is not CharacterArticle characterArticle) return null;

        var articlesContainer = GlobalUtil.GetBattleFieldCoreNode<ArticlesContainer>(owner);
        if (articlesContainer == null) return null;

        var opponentList = articlesContainer.GetOpponentArticles(owner);
        if (opponentList.Count == 0) return null;

        List<Vector2I> targetPointList = new();
        foreach (var opponent in opponentList)
        {
            var directions = new List<Vector2I>
            {
                opponent.TilePosition + Vector2I.Right,
                opponent.TilePosition + Vector2I.Left,
                opponent.TilePosition + Vector2I.Down,
                opponent.TilePosition + Vector2I.Up,
            };

            targetPointList.AddRange(directions.Where(direction => tileMapLayer.GetUsedRect().HasPoint(direction) && !_aStar2D.IsPointSolid(direction)));
        }

        if (targetPointList.Count == 0) return null;

        targetPointList.Sort((a, b) => (a - characterArticle.TilePosition).LengthSquared().CompareTo((b - characterArticle.TilePosition).LengthSquared()));
        var path = _aStar2D.GetIdPath(characterArticle.TilePosition, targetPointList[0], true);

        if (path.Count < 2) return null;

        return path.Count > 1 ? path[1] : null;
    }

    private Constants.BtStatus ActionExecuted()
    {
        _targetPosition = null;
        _moveTween?.Kill();
        _moveTween = null;
        _elapsedTime = 0;
        return Constants.BtStatus.Success;
    }

    protected override Constants.BtStatus PerformAction(double delta, Node owner)
    {
        if (owner is not ArticleBase article) return Constants.BtStatus.Failure;

        if (_moveTween != null)
        {
            if (!_moveTween.CustomStep(_elapsedTime))
            {
                article.TilePosition = _targetPosition!.Value;
                article.AnimationPlayer.Play("Idle");
                return ActionExecuted();
            }

            _elapsedTime += delta;
            return Constants.BtStatus.Running;
        }

        var tileMapLayer = GlobalUtil.GetBattleFieldCoreNode<BattleFieldTileMapLayer>(article);

        if (tileMapLayer == null)
        {
            throw new NullReferenceException("TileMapLayer is null");
        }

        _targetPosition ??= FindTarget(article, tileMapLayer);

        if (_targetPosition == null) return ActionExecuted();

        Vector2 to = tileMapLayer.ToGlobal(tileMapLayer.MapToLocal(_targetPosition.Value));

        _moveTween = article.CreateTween();
        _moveTween.TweenProperty(article, "global_position", to, 1f);
        _moveTween.Pause();
        article.AnimationPlayer.Play("Walk");

        return Constants.BtStatus.Running;
    }
}
```
