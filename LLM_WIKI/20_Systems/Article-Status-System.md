---
type: system
system: ArticleStatus
status: active
updated: 2026-07-08
---

# Article Status System

## Agent Brief

- 주요 위치: `Assets/Script/Article`, `Assets/Script/Article/Status`
- 책임: 게임 객체, 능력치, 지속 효과, 피해 처리
- 기반 객체: `ArticleBase : Node2D`
- 상태 컨테이너: `ArticleStatus : Resource`

## Model

- StatusElement: Health, Mana, Strength, Defense, Intelligence, Luck, Mobility, HealthRegen, ManaRegen
- StatusAffect: 물리/마법 피해 및 지속 효과
- ArticleBase: 위치, 생존, 이동/사망 signal, 애니메이션

## Turn-Start StatusElement

`ITurnStartStatusElement`(`Assets/Script/Article/Status/Element/ITurnStartStatusElement.cs`)를 구현한
StatusElement는 유닛 턴 시작에 스스로 반응한다.

```csharp
int TurnStartOrder { get; }
void ApplyTurnStart(ArticleStatus status);
```

- `ArticleStatus.ApplyTurnStartStatusElements()`는 `StatusElementsDictionary`에서 이 계약을 구현한 스탯을
  `TurnStartOrder` 오름차순(동점자는 타입 `FullName` ordinal)으로 1회씩 실행한다. 구체 스탯 타입은 알지 않으며,
  새 turn-start 스탯은 `ArticleStatus` 수정 없이 인터페이스 구현만으로 추가된다.
- 각 스탯 실행 직전에 `HasLivingHealth()`를 확인한다. 앞선 스탯이 사망을 만들면 같은 hook 안에서 뒤 스탯은
  실행되지 않는다.
- 대상 스탯 조회는 `ArticleStatus.TryGetStatusElement<T>()`를 쓴다.

현재 구현체(ADR-019):

| StatusElement | TurnStartOrder | 동작 |
| --- | ---: | --- |
| `HealthRegen` | 100 | `Health.CurrentHealth += Value` |
| `ManaRegen` | 110 | `Mana.CurrentMana += Value` |

두 스탯 모두 `[Export] int Value`(기본 0)를 가진다. 회복은 기존 `Health`/`Mana` setter를 통해 적용하므로
clamp, signal, HealthBar 갱신, 사망 처리를 재사용한다. `Value == 0`이거나 대상 스탯(`Health`/`Mana`)이 없으면
no-op이다. 음수 `Value`는 감소로 적용되며, 음수 `HealthRegen`은 사망을 만들 수 있다. 회복은 RNG를 소비하지 않는다.

## Flow

TurnAction이 대상의 `ArticleStatus.ApplyAffectStatus()`를 호출한다. 즉시 효과는 바로 적용되고 지속 효과는
목록에 추가돼 이후 적용된다.

`CharacterArticle.ApplyTurnStartEffects()`는 턴 시작 순서만 오케스트레이션한다.

1. `StatusController.OnTurnStart()`
2. `ArticleStatus.ApplyTurnStartStatusElements()` (자연 회복)
3. 살아 있으면 `ArticleStatus.ApplyAffectingStatuses()` (지속 효과)

자연 회복이 지속 피해보다 먼저다. 자연 회복이나 지속 피해로 사망한 유닛은 같은 hook 안에서 HP가 다시
양수가 되지 않는다.

지속 효과 목록(`AffectingStatusesList`)은 이 hook을 통해 해당 유닛의 턴 시작 시 1회 적용된다. Running action
frame 반복, 애니메이션 길이, `TurnHelper.Speed`는 지속 효과나 자연 회복의 적용 횟수를 늘리지 않는다.

## Production Wiring

전투 캐릭터 씬은 `ArticleStatus.StatusElements` 배열에 `Mana`/`ManaRegen`/`HealthRegen`을 가진다.

| Unit | MaxMana | ManaRegen | HealthRegen |
| --- | ---: | ---: | ---: |
| PrincessKnight | 60 | 8 | 0 |
| Puppet | 30 | 3 | 0 |
| TempArticle2 | 40 | 5 | 0 |
| TempArticle3 | 60 | 8 | 0 |

`battle_field.tscn`은 각 캐릭터 인스턴스의 `ArticleStatus`를 로컬 sub-resource로 **override**한다(Ally 1 +
Opponent 4). 캐릭터 씬만 고치면 전투 경로에 반영되지 않으므로, 스탯을 추가할 때 override 5개도 함께 고쳐야 한다.
현재 override는 Ally = TempArticle3 값, Opponent 4명 = Puppet 값으로 배선돼 있다.

`HealthRegen`은 전 캐릭터 0이라 no-op이며, 전투 UI에 마나 바는 아직 없다.

## Damage RNG

`PhysicalDamage`는 크리티컬 판정과 데미지 롤에 Godot 전역 RNG를 쓰지 않고,
`BattleFieldScene.BattleField.TurnHelper`의 전투 `CombatRng`를 소비한다. 같은 seed와 같은 판정 순서에서는
같은 크리티컬/데미지 시퀀스가 재현된다.

## Known Risks

- 필수 StatusElement가 없는 리소스의 dictionary 인덱서 접근(`StatusElementsDictionary[typeof(T)]`)은
  `KeyNotFoundException`을 던진다. 조회는 `TryGetStatusElement<T>()`, 생존 판정은 `HasLivingHealth()`를 쓴다.
  `ArticleBase.IsAlive`는 `HasLivingHealth()`에 위임한다.
- StatusElements 배열 null 또는 중복 타입(`InitStatus`의 `Dictionary.Add`가 예외).
- 사망 signal과 QueueFree의 순서
- Resource 공유로 인한 여러 캐릭터 간 상태 공유 여부. 씬의 StatusElement sub-resource는
  `resource_local_to_scene = true`여야 인스턴스별로 분리된다.
- 만료(`Cost == 0`)된 `StatusAffect`는 `Apply()` 안에서 `OnAffectedEnd` → `RemoveAffectStatus()`로
  `AffectingStatusesList`를 수정한다. `ApplyAffectingStatuses()`는 이 때문에 리스트 snapshot을 순회한다.
  같은 턴에 만료된 효과는 그 턴에 1회 적용된 뒤 목록에서 빠진다.

## Verification

- `Assets/Script/Tests/st001_step1_natural_regen_test.tscn`: turn-start dispatch 정확히 1회 적용, max clamp,
  음수 감소, 누락 스탯 no-op, 사망 뒤 회복/지속 효과 스킵, `TurnHelper` frame/Speed 불변,
  커스텀 `ITurnStartStatusElement` 확장성과 `TurnStartOrder` 순서, 만료되는 지속 효과의 순회 안전성,
  `IsAlive` ↔ `HasLivingHealth()` 위임(`Health` 없는 Article 포함).
- `Assets/Script/Tests/st001_step2_production_wiring_test.tscn`: 캐릭터 씬 4종과 `battle_field.tscn` 전투
  인스턴스 5개의 배선/중복 없음, `InitStatus` 후 `Mana.CurrentMana == MaxMana`, 턴당 1회 회복과 clamp,
  씬 저장/재로드 왕복 보존, `ManaCost` 지불 후 다음 턴 회복.

## Related

- [[Turn-System]]
- [[Skill-System]]
- [[ADR-019-Natural-Regen-Stats]]
- [[ST-001-Natural-Regen-Stats]]
- [[CB-001-Deterministic-Combat-Resolution]]