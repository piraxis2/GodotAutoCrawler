#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status;
using AutoCrawler.Assets.Script.Article.Status.Element;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;
using AutoCrawler.Assets.Script.SkillSystem;
using AutoCrawler.Assets.Script.SkillSystem.Blocks;
using AutoCrawler.Assets.Script.TurnAction;
using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script.Tests;

public partial class St001Step2ProductionWiringTest : Node
{
    private const string BattleScenePath = "res://Assets/Scenes/Map/battle_field.tscn";

    // ST-001 Step 2 production 배선 기대값. Task의 Production Defaults 표와 같아야 한다.
    private static readonly ExpectedWiring[] Expected =
    {
        new("res://Assets/Scenes/Character/PrincessKnight.tscn", 60, 8, 0),
        new("res://Assets/Scenes/Character/Puppet.tscn", 30, 3, 0),
        new("res://Assets/Scenes/Character/Temp/TempArticle2.tscn", 40, 5, 0),
        new("res://Assets/Scenes/Character/Temp/TempArticle3.tscn", 60, 8, 0)
    };

    private int _failures;

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[ST-001 Step2] Running production wiring tests...");
        try
        {
            TestSceneWiringAndInit();
            TestTurnStartRegenOnProductionScenes();
            TestSceneSaveReloadPreservesStats();
            await TestManaCostPaymentThenNextTurnRegen();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[ST-001 Step2] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            SetBattleFieldScene(null);
        }

        if (_failures == 0)
        {
            GD.Print("[ST-001 Step2] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[ST-001 Step2] FAILED: {_failures} assertion(s)");
            GetTree().Quit(1);
        }
    }

    private void Check(string name, bool actual, bool expected)
    {
        if (actual == expected) GD.Print($"  PASS: {name}");
        else { _failures++; GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}"); }
    }

    private void CheckEqual<T>(string name, T actual, T expected)
    {
        if (EqualityComparer<T>.Default.Equals(actual, expected)) GD.Print($"  PASS: {name}");
        else { _failures++; GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}"); }
    }

    // 씬 export 배열에 Mana/ManaRegen/HealthRegen이 중복 없이 들어 있고, InitStatus가 예외 없이 통과한다.
    private void TestSceneWiringAndInit()
    {
        GD.Print("[A] Production scene StatusElements wiring");
        foreach (var expected in Expected)
        {
            var article = InstantiateArticle(expected.ScenePath);
            string tag = expected.Name;

            var elements = GetExportedStatusElements(article.ArticleStatus);
            CheckEqual($"A.{tag}.NoDuplicateStatusTypes", elements.Select(e => e.GetType()).Distinct().Count(), elements.Count);

            article.ArticleStatus.InitStatus(article);
            AssertWiredValues(tag, article, expected);

            article.Free();
        }
    }

    // 전투 시작(InitStatus) 시 Mana는 Max이고, 턴 시작 hook 1회당 ManaRegen만큼 1회 회복된다.
    private void TestTurnStartRegenOnProductionScenes()
    {
        GD.Print("[B] Turn-start regen on production scenes");
        foreach (var expected in Expected)
        {
            var article = InstantiateArticle(expected.ScenePath);
            string tag = expected.Name;
            article.ArticleStatus.InitStatus(article);

            var mana = Get<Mana>(article);
            var health = Get<Health>(article);
            int hpBefore = health.CurrentHealth;

            // Max에서 시작하면 clamp에 가려 회복이 보이지 않는다. 2턴을 회복해도 Max에 닿지 않도록 여유를 만든다.
            int start = expected.MaxMana - (expected.ManaRegen * 2 + 1);
            mana.CurrentMana = start;
            article.ApplyTurnStartEffects();
            CheckEqual($"B.{tag}.ManaRegeneratedOnce", mana.CurrentMana, start + expected.ManaRegen);

            article.ApplyTurnStartEffects();
            CheckEqual($"B.{tag}.ManaRegeneratedTwiceOnSecondTurn", mana.CurrentMana, start + expected.ManaRegen * 2);

            // HealthRegen이 0이므로 CB-001 결정론 회귀에 영향을 주는 HP 변화가 없어야 한다.
            CheckEqual($"B.{tag}.HealthUnchangedByZeroRegen", health.CurrentHealth, hpBefore);

            // Max clamp 보존.
            mana.CurrentMana = expected.MaxMana - 1;
            article.ApplyTurnStartEffects();
            CheckEqual($"B.{tag}.ManaClampedAtMax", mana.CurrentMana, expected.MaxMana);

            article.Free();
        }
    }

    // .tscn 재직렬화 왕복 후에도 새 스탯과 값이 export 배열에 보존된다(설계 리뷰 [P3]).
    private void TestSceneSaveReloadPreservesStats()
    {
        GD.Print("[C] Scene save/reload round-trip preserves new stats");
        foreach (var expected in Expected)
        {
            string tag = expected.Name;
            var packed = GD.Load<PackedScene>(expected.ScenePath);
            string roundTripPath = $"user://st001_step2_roundtrip_{tag}.tscn";

            Error saveError = ResourceSaver.Save(packed, roundTripPath);
            CheckEqual($"C.{tag}.SaveOk", saveError, Error.Ok);

            var reloaded = ResourceLoader.Load<PackedScene>(roundTripPath, cacheMode: ResourceLoader.CacheMode.Ignore);
            Check($"C.{tag}.ReloadOk", reloaded != null, true);
            if (reloaded == null) continue;

            var article = reloaded.Instantiate<CharacterArticle>();
            var elements = GetExportedStatusElements(article.ArticleStatus);
            CheckEqual($"C.{tag}.NoDuplicateStatusTypesAfterReload", elements.Select(e => e.GetType()).Distinct().Count(), elements.Count);

            article.ArticleStatus.InitStatus(article);
            AssertWiredValues($"C.{tag}", article, expected);

            article.Free();
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(roundTripPath));
        }
    }

    // production 캐릭터가 ManaCost 스킬 비용을 실제 Mana에서 지불하고, 다음 턴 시작에 ManaRegen으로 회복한다.
    private async Task TestManaCostPaymentThenNextTurnRegen()
    {
        GD.Print("[D] ManaCost payment then next-turn regen on battle_field");
        var battle = await LoadBattle(4242);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");

            // battle_field.tscn은 각 인스턴스의 ArticleStatus를 로컬 sub-resource로 override한다.
            // 원본 캐릭터 씬만 배선하면 실제 전투 경로에는 Mana가 없으므로, 전투 인스턴스 전부를 직접 검사한다.
            AssertBattleFieldInstancesWired(battle);

            caster.TilePosition = new Vector2I(5, 5);
            target.TilePosition = new Vector2I(5, 6);
            IsolateOtherOpponents(battle, target);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // battle_field의 Ally는 TempArticle3다. AddMana fixture 없이 production 배선만으로 Mana가 있어야 한다.
            var mana = caster.ArticleStatus.StatusElementsDictionary.GetValueOrDefault(typeof(Mana)) as Mana;
            Check("D.CasterHasProductionMana", mana != null, true);
            if (mana == null) return;

            CheckEqual("D.CombatStartsAtMaxMana", mana.CurrentMana, mana.MaxMana);
            var manaRegen = caster.ArticleStatus.StatusElementsDictionary.GetValueOrDefault(typeof(ManaRegen)) as ManaRegen;
            Check("D.CasterHasProductionManaRegen", manaRegen != null, true);
            if (manaRegen == null) return;

            var definition = new SkillDefinition
            {
                Id = new StringName("st001_step2_mana_cost"), Range = 3, ManaCost = 5,
                TargetSide = SkillTargetSide.Enemy, TargetSelectorDefault = SkillTargetSelector.Nearest,
                Effects = new Array<EffectBlock>
                {
                    new DamageBlock { MinDamage = 1, MaxDamage = 1, DamageType = SkillDamageType.Physical, HitChance = 100 }
                }
            };
            var action = new TurnAction_Skill { Definition = definition };
            var ownerNode = MakeActionOwner(caster);

            mana.CurrentMana = 30;
            action.Init(ownerNode);
            var state = caster.CurrentTurnActionState as SkillState;
            Check("D.SkillRunnableWithProductionMana", state is { Failed: false }, true);
            CheckEqual("D.ManaSpent", mana.CurrentMana, 25);
            RunToEnd(action, caster);

            // 다음 턴 시작: 지불한 마나가 ManaRegen만큼 돌아온다.
            caster.ApplyTurnStartEffects();
            CheckEqual("D.ManaRegeneratedNextTurn", mana.CurrentMana, 25 + manaRegen.Value);

            ownerNode.Free();
        }
        finally
        {
            await FreeBattle(battle);
        }
    }

    // battle_field.tscn의 override된 ArticleStatus에도 새 스탯이 배선돼 있어야 한다.
    // Ally는 TempArticle3(60/8/0), Opponent 4명은 Puppet(30/3/0) 기준이다.
    private void AssertBattleFieldInstancesWired(BattleFieldScene battle)
    {
        var teams = new (string Path, int MaxMana, int ManaRegen)[]
        {
            ("Articles/Ally", 60, 8),
            ("Articles/Opponent", 30, 3)
        };

        foreach (var (path, maxMana, manaRegen) in teams)
        {
            foreach (var article in battle.GetNode(path).GetChildren().Cast<CharacterArticle>())
            {
                string tag = $"D.{article.Name}";
                var dictionary = article.ArticleStatus.StatusElementsDictionary;
                Check($"{tag}.HasMana", dictionary.ContainsKey(typeof(Mana)), true);
                Check($"{tag}.HasManaRegen", dictionary.ContainsKey(typeof(ManaRegen)), true);
                Check($"{tag}.HasHealthRegen", dictionary.ContainsKey(typeof(HealthRegen)), true);
                if (!dictionary.ContainsKey(typeof(Mana)) || !dictionary.ContainsKey(typeof(ManaRegen))) continue;

                var mana = Get<Mana>(article);
                CheckEqual($"{tag}.MaxMana", mana.MaxMana, maxMana);
                CheckEqual($"{tag}.CombatStartsAtMaxMana", mana.CurrentMana, maxMana);
                CheckEqual($"{tag}.ManaRegenValue", Get<ManaRegen>(article).Value, manaRegen);
                CheckEqual($"{tag}.HealthRegenValue", Get<HealthRegen>(article).Value, 0);
            }
        }
    }

    private void AssertWiredValues(string tag, CharacterArticle article, ExpectedWiring expected)
    {
        var dictionary = article.ArticleStatus.StatusElementsDictionary;
        Check($"{tag}.HasHealth", dictionary.ContainsKey(typeof(Health)), true);
        Check($"{tag}.HasMana", dictionary.ContainsKey(typeof(Mana)), true);
        Check($"{tag}.HasManaRegen", dictionary.ContainsKey(typeof(ManaRegen)), true);
        Check($"{tag}.HasHealthRegen", dictionary.ContainsKey(typeof(HealthRegen)), true);

        var mana = Get<Mana>(article);
        CheckEqual($"{tag}.MaxMana", mana.MaxMana, expected.MaxMana);
        CheckEqual($"{tag}.CombatStartsAtMaxMana", mana.CurrentMana, expected.MaxMana);
        CheckEqual($"{tag}.ManaRegenValue", Get<ManaRegen>(article).Value, expected.ManaRegen);
        CheckEqual($"{tag}.HealthRegenValue", Get<HealthRegen>(article).Value, expected.HealthRegen);
    }

    private static CharacterArticle InstantiateArticle(string scenePath)
    {
        // SceneTree에 넣지 않으므로 _Ready(GetNode/InitStatus)가 돌지 않는다. InitStatus는 테스트가 직접 호출한다.
        return GD.Load<PackedScene>(scenePath).Instantiate<CharacterArticle>();
    }

    private static TStatus Get<TStatus>(CharacterArticle article) where TStatus : StatusElement
    {
        return article.ArticleStatus.StatusElementsDictionary[typeof(TStatus)] as TStatus;
    }

    private static List<StatusElement> GetExportedStatusElements(ArticleStatus status)
    {
        var property = typeof(ArticleStatus).GetProperty("StatusElements", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var array = (Array<StatusElement>)property.GetValue(status);
        return array.ToList();
    }

    private static void RunToEnd(TurnAction_Skill action, CharacterArticle caster)
    {
        for (int i = 0; i < 16; i++)
        {
            if (action.Action(100.0, caster) == ActionState.End) break;
        }
    }

    private async Task<BattleFieldScene> LoadBattle(long seed)
    {
        var packed = GD.Load<PackedScene>(BattleScenePath);
        var battle = packed.Instantiate<BattleFieldScene>();
        var turnHelper = battle.GetNode<TurnHelper>("TurnHelper");
        typeof(TurnHelper).GetField("_combatSeed", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(turnHelper, seed);
        typeof(TurnHelper).GetProperty(nameof(TurnHelper.Speed))!
            .SetValue(turnHelper, 0.0f);

        AddChild(battle);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        return battle;
    }

    private async Task FreeBattle(BattleFieldScene battle)
    {
        battle.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        SetBattleFieldScene(null);
    }

    // battle_field에는 상대가 4명 있으므로, 지정 대상만 남기고 나머지를 사거리 밖으로 밀어 대상 확정을 고정한다.
    private static void IsolateOtherOpponents(BattleFieldScene battle, CharacterArticle keep)
    {
        int far = 900;
        foreach (var opponent in battle.GetNode("Articles/Opponent").GetChildren().Cast<CharacterArticle>())
        {
            if (opponent != keep) { opponent.TilePosition = new Vector2I(far, far); far++; }
        }
    }

    private static BehaviorTree_TurnAction MakeActionOwner(CharacterArticle caster)
    {
        var ownerNode = new BehaviorTree_TurnAction { Name = "St001Step2ActionOwner" };
        typeof(BehaviorTree_Node).GetProperty(nameof(BehaviorTree_Node.Tree))!
            .SetValue(ownerNode, caster.BehaviorTree);
        return ownerNode;
    }

    private static void SetBattleFieldScene(BattleFieldScene battleField)
    {
        typeof(BattleFieldScene)
            .GetField("_battleFieldScene", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, battleField);
    }

    private readonly record struct ExpectedWiring(string ScenePath, int MaxMana, int ManaRegen, int HealthRegen)
    {
        public string Name => ScenePath.GetFile().GetBaseName();
    }
}
#endif
