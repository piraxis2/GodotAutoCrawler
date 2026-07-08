---
type: system
system: Skill
status: complete
updated: 2026-07-08
---

# Skill System

## Agent Brief

- 주요 위치: `Assets/Script/SkillSystem`, `Assets/SkillData/slash.tres`
- 책임: 스킬 정의(`SkillDefinition`)를 데이터 Resource로 두고, 실행 중 상태를 유닛별 `SkillState`로 분리해 기존 BT/TurnAction 경로에서 실행한다.
- 현재 상태: **SK-001 Step 0~6 전체 완료**. 데이터 기반 스킬 골격 + 블록 카탈로그 + Phase1 12종 데이터. 후속(별도 Task): 기존 3종 TurnAction BT 재배선/삭제, 무대상/환불 정책, damage-result API. `Mana` production 배선은 ST-001 Step 2에서 완료.

## Model

- `SkillDefinition : Resource`: v0 필드 `Id`, `DisplayName`, `Range`, `ActCost`, `WindupCost`, `AnimationName`, `TargetSide`, `TargetSelectorDefault`, `Effects`. `MasterCost = max(ActCost + WindupCost, 1)`.
- `EffectBlock : Resource`: 불변 파라미터 Resource. `EnqueuePhases(SkillContext, SkillState)`로 런타임 phase를 `SkillState`에 추가한다.
- `SkillState`: 일반 C# 객체. phase queue, used cost, confirmed targets, report를 보유하며 `.tres` 저장 대상이 아니다.
- `SkillContext`: Step 1에서는 `BattleFieldScene.BattleField` adapter다. block public 계약은 context 주입형으로 시작했지만, `DamageBlock`은 baseline 일치를 위해 legacy `Damage.CreateDamage<T>()`를 호출한다.
- `TurnAction_Skill : TurnActionBase`: export `SkillDefinition`을 실행하는 v0 실행기. 상태는 caster `CharacterArticle.CurrentTurnActionState`에 저장한다.

## Target Selection (Step 2)

- `TargetSide`: `Enemy`(상대), `Ally`(자신 제외 아군), `Self`(사거리/필터 우회, 시전자 확정).
- `TargetSelector`: `Nearest`(정규 순서 최근접), `LowestHp`/`HighestHp`(HP 우선), `Self`(side와 무관하게 시전자 확정).
- 모든 동점자는 ADR-017 정규 순서(Manhattan 거리 -> Y -> X)로 고정한다. `LowestHp`/`HighestHp`도 HP 정렬 후 `SkillUtil.ThenByCanonical`을 써 유클리드 편차 없이 정규 순서를 공유한다.
- 사거리 내 무대상이면 `SelectTarget`이 `null`(현재 no-op로 턴 소모). 무대상 fail-closed는 Step 3에서 마나/명중 실패 신호와 함께 고정한다.

## Windup (Step 2)

- `WindupCost > 0`이면 `Init`이 그 횟수만큼 `CastingPhase`를 큐 앞에 넣고 `SkillState.IsCasting = true`로 둔다. 마지막 windup phase 소비 후 `IsCasting = false`.
- `CastingPhase`는 첫 프레임 `Cast`(이후 `Casting`) 애니메이션을 재생하고 완료 시 `ActionState.Executed`(End 아님)를 반환한다. `Action`이 `CurrentTurnActionState`를 비우지 않으므로 `CharacterArticle.TurnPlay`가 다음 턴에 `CurrentTurnAction.Action`을 직접 구동(BT `Behave` 우회)해 남은 phase를 재개한다. legacy `TurnAction_Cast`와 등가 턴 소비.
- `magicbolt.tres`/`마탄`: range 3, `WindupCost 1`, `DamageBlock` magical(고정 `MaxDamage`), `MasterCost 2`. 직접 Action 루프 기준선 `[Running, Executed, End]`이 legacy `TurnAction_MagicBolt`와 일치.
- `slash.tres`/`베기`: range 1, `DamageBlock` physical 10~20, animation `Attack`, windup 없음.
- invalid definition(`null`, empty id, negative range, null effect)은 state를 만들지 않고 `ActionState.End`로 fail-closed된다.
- 기존 `TurnAction_Attack`, `TurnAction_MagicBolt`, `TurnAction_ChainLightning`은 삭제하지 않았다.

## Payment Gate and Hit Chance (Step 3)

- `Mana : StatusElement`: `CurrentMana`/`MaxMana`, `CanAfford`, `TrySpend`(부족 시 소모 없이 false). production character scene과 `battle_field.tscn` override 모두 배선 완료(ST-001 Step 2). 전투 시작 시 `CurrentMana == MaxMana`이고, 턴 시작마다 `ManaRegen`만큼 회복된다([[Article-Status-System]]).
- `SkillDefinition.ManaCost`: `TurnAction_Skill.Init`이 `ManaCost > 0`일 때만 `Mana.TrySpend`로 지불. 부족하면 상태 변경 없이 `SkillState.Failed`를 세우고, `Action`이 `ActionState.Failure`로 반환한다. `ManaCost == 0`(slash/magicbolt)은 게이트 없음 → 기존 baseline 보존.
- `ActionState.Failure`: 지불/검증 실패 신호. `BehaviorTree_TurnAction.PerformAction`과 `CharacterArticle.TurnPlay`가 `BtStatus.Failure`로 매핑하고 CurrentTurnAction을 세우지 않아 `BehaviorTree_Selector`가 다음 행으로 fallback한다. legacy 3종은 `Failure`를 반환하지 않아 additive.
- `DamageBlock.HitChance`(0~100): `HitChance < 100`일 때만 `SkillContext.CombatRandRange(0,100) >= HitChance`로 명중 판정(단일 CombatRng 1회). RNG 순서 `명중 -> 크리티컬 -> 피해`, 같은 seed 재현. `HitChance == 100`은 hit roll 생략(baseline RNG 스트림 보존). 명중 실패/성공 모두 `SkillState.Reports`에 `miss:`/`damage:` 기록.
- 피해 금액 산출 + Floater/Hit UI는 아직 legacy `Damage.CreateDamage<T>()`/`ApplyImmediately`. `DamageBlock.ApplyDamage`가 UI 결합 제거·headless 피해 result API로 넘길 migration 경계다.

## Status / Control Blocks (Step 4a)

- `StatusController`(유닛별, `CharacterArticle.StatusController`): 턴 카운터로 제어/버프 상태 보유. `OnTurnStart`(턴 시작 틱, `ApplyTurnStartEffects`가 호출)와 `ConsumeStunForTurn`(TurnPlay 턴당 1회)로 frame 비종속. 기존 `StatusAffect`(Damage 지향, 1턴 제어 apply/expire 붕괴) 대신 사용.
- `StunBlock`: 대상 스턴(`ApplyStun`). `IsCasting` 대상은 `CancelCasting`으로 영창 취소(비용/마나 미환불). TurnPlay가 스턴 턴을 즉시 Success로 스킵.
- `BindBlock`: 대상 이동 봉쇄(`ApplyBind`). `OnTurnStart`가 `BoundThisTurn` 확정 → `BehaviorTree_Move`/`BehaviorTree_MultipleMove`가 이동 없이 `ActionExecuted()`(Success).
- `SelfBuffBlock`: 시전자(`context.Caster`)에 주는 피해 배율 버프(`ApplyDamageDealtBuff`).
- `ManaDrainBlock`: 대상 `Mana` 감소 + (옵션) 시전자 `Mana` 회복.
- 피해 배율 hook: `ArticleBase.DamageDealtMultiplier`/`DamageTakenMultiplier`(virtual, 기본 1.0)를 `CharacterArticle`이 `StatusController`로 오버라이드. `Damage.ApplyImmediately`가 롤 이후 곱셈 → RNG 무이동, 기본 1.0 baseline 보존.
- `KnockbackBlock`(Step 4b): 대상을 시전자 반대 4방향(더 큰 축, 동률 X)으로 최대 `Distance`칸 밀되 맵 경계(`GetUsedRect().HasPoint`)·점유 칸(`GetArticle`)에서 중단. 최종 위치를 `TilePosition`으로 1회 설정 → `OnMove` → tilemap `_placedArticles` → (다음)`UpdateAStar` 갱신. 별도 점유 테이블 없이 기존 파이프라인 재사용.
- `ChainBlock`(Step 5): `TurnAction_ChainLightning` 이관. 첫 대상은 `ConfirmedTargets[0]`(=legacy `GetTarget`), 이후 각 홉마다 현재 대상 위치 기준 사거리 안에서 `미타격 우선 -> ADR-017 정규 순서`(legacy `GetChainTarget`과 동일 코드)로 다음 대상 선택. 고정 `MagicalDamage`(RNG 미소비), DamageBlock처럼 단일 phase에 연쇄 적용. legacy와 대상별 HP 결과 완전 일치. `TurnAction_ChainLightning`은 아직 TempArticle3/PrincessKnight BT에 배선돼 있어 삭제 안 함(Step 6).
## Phase1 Data Pack (Step 6)

- `Assets/SkillData/Phase1/*.tres`: 12종(검/격투/활/지팡이 × 3). 블록 조합으로 구성, `07_수치표_Phase1.md` 기준.
  - 기본기: `sword_slash`(베기 10~20/95), `fist_jab`(잽 10~16/100), `bow_shot`(사격 10~18/90 R5), `staff_magicbolt`(마탄 Mag15/95 R3 마나5).
  - 강기: `sword_smash`(강타 Dmg+Knockback1+Stun20%), `fist_straight`, `fist_sonicblow`(30~45+Stun35% windup1), `bow_aimedshot`(windup1 명중100), `staff_chainlightning`(ChainBlock 3체 windup1 마나30).
  - 제어: `sword_ironbody`(금강체 SelfBuff Taken0.5×3 Self), `bow_bindingarrow`(속박 Bind3).
  - 흡수: `staff_manaabsorb`(Mag8~15 + ManaDrain16).
- 블록 확장: `StunBlock.Chance`(0~100, <100일 때만 CombatRng 1회, 기본 100 무roll), `SelfBuffBlock.Kind`(Dealt/Taken, 기본 Dealt).
- 근사/미모델: 조준 사격 크리 보너스·연쇄 뇌격 per-hit 명중·마나 흡수 정확한 50%는 후속(damage-result API 등). 마법 min 미반영(D-2 준수). `combo` 필드/데이터 없음.
- 기존 3종 하드코딩 TurnAction(`Attack`→TempArticle2, `MagicBolt`→씬 참조 없음, `ChainLightning`→PrincessKnight/TempArticle3)은 데이터 스킬로 대체 가능하나 BT 재배선/삭제는 별도 cleanup으로 이월(밸런스 스왑·CB-001 회귀).

## Compatibility Seam

`TurnActionBase.Init`/`Finish`/`Action`은 virtual로 열렸다. 기존 구현은 그대로 base 동작을 사용한다.
`CharacterArticle.CurrentTurnActionState`는 `ITurnActionState` 타입의 단일 런타임 슬롯이며 `TurnAction_Skill`의 `SkillState`를 저장한다. 기존 `CurrentTurnAction` 재개 경로는 유지된다.
영창 상태는 `ITurnActionState.IsCasting` getter와 `CharacterArticle.IsCasting` 창구로 SkillSystem 구체 타입 의존 없이 외부(HUD/조건 어휘/영창 취소)에서 소비 가능하다. 실제 소비처는 Step 4.

## Verification

- `Sk001Step1SkillSystemTest`: slash `.tres` load/save/reload, legacy Attack baseline, shared Resource SkillState isolation, invalid definition fail-closed.
- `Sk001Step2WindupTest`: selector/side 매트릭스(`Nearest`/`LowestHp`/`HighestHp`/`Self`), 동점 정규 순서(`A.TieCanonical`, 유클리드/Manhattan 구분), legacy MagicBolt baseline, windup 후 BT 우회/재개.
- `Sk001Step3ManaHitTest`: 마나 게이트(부족→Failure·무변경, 충분→지불·피해), HitChance 0/100 miss/hit + report sink, miss가 CombatRng 정확히 1회 소비·부분 명중 seed 재현, `PerformAction`/`TurnPlay` Bt Failure fallback.
- `Sk001Step4StatusControlTest`: StatusController apply/만료/재진입(순수)+turns<=0 no-op, StunBlock+TurnPlay 스킵, BindBlock 이동 차단, SelfBuff 배율, ManaDrain, 스턴 영창 취소(무환불), 턴 순환 무교란.
- `Sk001Step4bKnockbackTest`: 넉백 이동/맵 경계/점유 충돌/방향(카디널) + `OnMove` 발행 + tilemap/AStar 점유 갱신.
- `Sk001Step5ChainTest`: legacy ChainLightning vs ChainBlock 대상별 HP baseline 일치 + 체인 선택 미타격 우선.
- `Sk001Step6DataPackTest`: 커밋 12종 읽기 전용 로드 + 정본(`Phase1SkillSpec`)과 전체 필드·이펙트 파라미터 서명 비교(누락/드리프트 검출) + 직렬화 round-trip + 기본기·강기·제어·흡수 실행 + StunBlock Chance 0/100 결정론. `.tres` 생성은 별도 부트스트랩 `Sk001Phase1DataGenerator`(회귀 비포함, 수동).
- CB-001 Step 1~4 회귀 유지.

## Related

- [[SK-001-Data-Driven-Skill-System]]
- [[SK-001-Data-Driven-Skill-System-Review]]
- [[ADR-018-Data-Driven-Skill-System]]
- [[Turn-System]]
- [[Article-Status-System]]
- [[BehaviorTree-System]]