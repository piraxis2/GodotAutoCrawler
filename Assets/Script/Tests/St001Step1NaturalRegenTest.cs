#if TOOLS
using System;
using System.Collections.Generic;
using System.Reflection;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Interface;
using AutoCrawler.Assets.Script.Article.Status;
using AutoCrawler.Assets.Script.Article.Status.Affect;
using AutoCrawler.Assets.Script.Article.Status.Element;
using AutoCrawler.Assets.Script.TurnAction;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

public partial class St001Step1NaturalRegenTest : Node
{
    private int _failures;

    public override void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[ST-001 Step1] Running natural regen tests...");
        try
        {
            TestNaturalRegenAppliesExactValuePerCall();
            TestNaturalRegenAppliesAndClamps();
            TestMissingStatsNoop();
            TestNegativeManaRegenDecreases();
            TestNegativeHealthRegenCanKillAndStopsMana();
            TestCharacterTurnStartOrderAndDeathGuard();
            TestTurnStartRegenIsFrameAndSpeedInvariant(1.0f);
            TestTurnStartRegenIsFrameAndSpeedInvariant(4.0f);
            TestCustomTurnStartElementRunsInOrderWithoutArticleStatusChange();
            TestCustomTurnStartElementSkippedAfterDeath();
            TestExpiringAffectDoesNotBreakIteration();
            TestIsAliveMatchesHasLivingHealth();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[ST-001 Step1] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }

        if (_failures == 0)
        {
            GD.Print("[ST-001 Step1] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[ST-001 Step1] FAILED: {_failures} assertion(s)");
            GetTree().Quit(1);
        }
    }

    private void CheckEqual<T>(string name, T actual, T expected)
    {
        if (EqualityComparer<T>.Default.Equals(actual, expected)) GD.Print($"  PASS: {name}");
        else { _failures++; GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}"); }
    }

    private void TestNaturalRegenAppliesExactValuePerCall()
    {
        GD.Print("[A0] Natural regen applies exactly Value per call");
        var article = MakeArticle("A0");
        var health = AddHealth(article, max: 100, current: 50);
        var mana = AddMana(article, max: 30, current: 10);
        AddHealthRegen(article, 7);
        AddManaRegen(article, 5);

        article.ArticleStatus.ApplyTurnStartStatusElements();
        CheckEqual("A0.HealthAfterFirstCall", health.CurrentHealth, 57);
        CheckEqual("A0.ManaAfterFirstCall", mana.CurrentMana, 15);

        article.ArticleStatus.ApplyTurnStartStatusElements();
        CheckEqual("A0.HealthAfterSecondCall", health.CurrentHealth, 64);
        CheckEqual("A0.ManaAfterSecondCall", mana.CurrentMana, 20);

        article.Free();
    }

    private void TestNaturalRegenAppliesAndClamps()
    {
        GD.Print("[A] Natural regen applies once and clamps");
        var article = MakeArticle("A");
        var health = AddHealth(article, max: 100, current: 90);
        var mana = AddMana(article, max: 30, current: 28);
        AddHealthRegen(article, 20);
        AddManaRegen(article, 5);

        article.ArticleStatus.ApplyTurnStartStatusElements();
        CheckEqual("A.HealthClamped", health.CurrentHealth, 100);
        CheckEqual("A.ManaClamped", mana.CurrentMana, 30);

        article.ArticleStatus.ApplyTurnStartStatusElements();
        CheckEqual("A.SecondCallStillClamped", health.CurrentHealth, 100);

        article.Free();
    }

    private void TestMissingStatsNoop()
    {
        GD.Print("[B] Missing target stats no-op");
        var healthOnly = MakeArticle("BHealthOnly");
        AddHealth(healthOnly, max: 50, current: 20);
        AddManaRegen(healthOnly, 10);
        healthOnly.ArticleStatus.ApplyTurnStartStatusElements();
        CheckEqual("B.HealthUnchangedWithoutHealthRegen", GetHealth(healthOnly).CurrentHealth, 20);

        var manaOnly = MakeArticle("BManaOnly");
        AddMana(manaOnly, max: 20, current: 5);
        AddHealthRegen(manaOnly, 10);
        manaOnly.ArticleStatus.ApplyTurnStartStatusElements();
        CheckEqual("B.ManaUnchangedWithoutManaRegen", GetMana(manaOnly).CurrentMana, 5);

        var zeroRegen = MakeArticle("BZeroRegen");
        AddHealth(zeroRegen, max: 50, current: 20);
        AddMana(zeroRegen, max: 20, current: 5);
        AddHealthRegen(zeroRegen, 0);
        AddManaRegen(zeroRegen, 0);
        zeroRegen.ArticleStatus.ApplyTurnStartStatusElements();
        CheckEqual("B.ZeroHealthRegenNoop", GetHealth(zeroRegen).CurrentHealth, 20);
        CheckEqual("B.ZeroManaRegenNoop", GetMana(zeroRegen).CurrentMana, 5);

        healthOnly.Free();
        manaOnly.Free();
        zeroRegen.Free();
    }

    private void TestNegativeManaRegenDecreases()
    {
        GD.Print("[B2] Negative mana regen drains and clamps at 0");
        var article = MakeArticle("B2");
        AddHealth(article, max: 50, current: 30);
        var mana = AddMana(article, max: 20, current: 5);
        AddManaRegen(article, -3);

        article.ArticleStatus.ApplyTurnStartStatusElements();
        CheckEqual("B2.ManaDrained", mana.CurrentMana, 2);

        article.ArticleStatus.ApplyTurnStartStatusElements();
        CheckEqual("B2.ManaClampedAtZero", mana.CurrentMana, 0);

        article.Free();
    }

    private void TestNegativeHealthRegenCanKillAndStopsMana()
    {
        GD.Print("[C] Negative health regen can kill and stops later regen");
        var article = MakeArticle("C");
        var health = AddHealth(article, max: 50, current: 3);
        var mana = AddMana(article, max: 20, current: 5);
        AddHealthRegen(article, -5);
        AddManaRegen(article, 10);
        int deadCount = 0;
        article.OnDead += _ => deadCount++;

        article.ArticleStatus.ApplyTurnStartStatusElements();
        CheckEqual("C.HealthKilled", health.CurrentHealth, 0);
        CheckEqual("C.DeadSignalOnce", deadCount, 1);
        CheckEqual("C.ManaSkippedAfterDeath", mana.CurrentMana, 5);

        article.Free();
    }

    private void TestCharacterTurnStartOrderAndDeathGuard()
    {
        GD.Print("[D] CharacterArticle turn-start order + death guard");
        var article = MakeArticle("D");
        var health = AddHealth(article, max: 20, current: 6);
        AddHealthRegen(article, 5);
        var damage = new TestHealthDeltaAffect(-12);
        article.ArticleStatus.ApplyAffectStatus(damage);
        int deadCount = 0;
        article.OnDead += _ => deadCount++;

        article.ApplyTurnStartEffects();
        CheckEqual("D.RegenBeforeAffectThenDeath", health.CurrentHealth, 0);
        CheckEqual("D.AffectAppliedOnce", damage.ApplyCount, 1);
        CheckEqual("D.DeadOnce", deadCount, 1);

        var deadArticle = MakeArticle("DDead");
        var deadHealth = AddHealth(deadArticle, max: 20, current: 4);
        AddHealthRegen(deadArticle, -10);
        var skipped = new TestHealthDeltaAffect(10);

        deadArticle.ArticleStatus.ApplyAffectStatus(skipped);
        deadArticle.ApplyTurnStartEffects();
        CheckEqual("D.NaturalDeath", deadHealth.CurrentHealth, 0);
        CheckEqual("D.AffectSkippedAfterNaturalDeath", skipped.ApplyCount, 0);

        article.Free();
        deadArticle.Free();
    }

    // 회복 횟수는 turn start hook 호출 횟수에만 의존한다. Running frame 반복과 Speed는 영향을 주지 않는다.
    private void TestTurnStartRegenIsFrameAndSpeedInvariant(float speed)
    {
        GD.Print($"[E] Running frames and Speed={speed} do not change regen count");
        var article = MakeArticle($"E{speed}");
        var health = AddHealth(article, max: 100, current: 50);
        var mana = AddMana(article, max: 50, current: 10);
        AddHealthRegen(article, 6);
        AddManaRegen(article, 4);

        var current = new RegenTurnArticle(article, BtStatus.Running);
        var other = new RegenTurnArticle(MakeArticle($"EOther{speed}"), BtStatus.Running);
        var helper = MakeHelper(speed, current, other);

        InvokeAdvanceToNextTurn(helper);
        helper._PhysicsProcess(0.016);
        helper._PhysicsProcess(0.016);
        helper._PhysicsProcess(0.016);

        CheckEqual($"E.turn_start_count_speed_{speed}", current.TurnStartEffectCount, 1);
        CheckEqual($"E.health_speed_{speed}", health.CurrentHealth, 56);
        CheckEqual($"E.mana_speed_{speed}", mana.CurrentMana, 14);
        CheckEqual($"E.other_untouched_speed_{speed}", other.TurnStartEffectCount, 0);

        helper.Free();
        article.Free();
        other.Article.Free();
    }

    // 새 turn-start 스탯은 ITurnStartStatusElement 구현만으로 추가된다. ArticleStatus는 이 테스트를 위해 변경되지 않았다.
    private void TestCustomTurnStartElementRunsInOrderWithoutArticleStatusChange()
    {
        GD.Print("[F] Custom ITurnStartStatusElement dispatches by TurnStartOrder");
        var article = MakeArticle("F");
        var health = AddHealth(article, max: 100, current: 50);
        var mana = AddMana(article, max: 50, current: 10);
        AddHealthRegen(article, 6);
        AddManaRegen(article, 4);

        var log = new List<string>();
        var beforeRegen = AddCustomTurnStartElement<EarlyTurnStartProbe>(article, log);
        var afterRegen = AddCustomTurnStartElement<LateTurnStartProbe>(article, log);

        article.ArticleStatus.ApplyTurnStartStatusElements();

        CheckEqual("F.EarlyRanOnce", beforeRegen.ApplyCount, 1);
        CheckEqual("F.LateRanOnce", afterRegen.ApplyCount, 1);
        CheckEqual("F.Order", string.Join(">", log), "early>late");
        // 기존 회복 동작은 그대로다.
        CheckEqual("F.HealthStillRegenerated", health.CurrentHealth, 56);
        CheckEqual("F.ManaStillRegenerated", mana.CurrentMana, 14);
        // Early(90) < HealthRegen(100) < ManaRegen(110) < Late(200)
        CheckEqual("F.EarlySawPreRegenHealth", beforeRegen.ObservedHealth, 50);
        CheckEqual("F.LateSawPostRegenHealth", afterRegen.ObservedHealth, 56);

        article.Free();
    }

    private void TestCustomTurnStartElementSkippedAfterDeath()
    {
        GD.Print("[G] Custom turn-start element is skipped after death in the same hook");
        var article = MakeArticle("G");
        AddHealth(article, max: 20, current: 4);
        AddHealthRegen(article, -10);
        var log = new List<string>();
        var afterRegen = AddCustomTurnStartElement<LateTurnStartProbe>(article, log);

        article.ArticleStatus.ApplyTurnStartStatusElements();

        CheckEqual("G.LateSkippedAfterDeath", afterRegen.ApplyCount, 0);

        article.Free();
    }

    // ArticleBase.IsAlive는 ArticleStatus.HasLivingHealth()에 위임한다.
    // Health가 없는 Article에서 dictionary indexer 예외 없이 "살아 있음"을 돌려줘야 한다.
    private void TestIsAliveMatchesHasLivingHealth()
    {
        GD.Print("[I] IsAlive delegates to HasLivingHealth");
        var noHealth = MakeArticle("INoHealth");
        AddMana(noHealth, max: 20, current: 5);
        CheckEqual("I.NoHealthIsAlive", noHealth.IsAlive, true);

        var living = MakeArticle("ILiving");
        var health = AddHealth(living, max: 50, current: 10);
        CheckEqual("I.LivingIsAlive", living.IsAlive, true);

        health.CurrentHealth = 0;
        CheckEqual("I.DeadIsNotAlive", living.IsAlive, false);

        noHealth.Free();
        living.Free();
    }

    // 만료(Cost == 0)된 StatusAffect는 OnAffectedEnd -> RemoveAffectStatus()로 AffectingStatusesList를 수정한다.
    // ApplyAffectingStatuses()가 리스트를 직접 순회하면 여기서 InvalidOperationException이 난다.
    private void TestExpiringAffectDoesNotBreakIteration()
    {
        GD.Print("[H] Affect expiring during ApplyAffectingStatuses does not break iteration");
        var article = MakeArticle("H");
        var health = AddHealth(article, max: 100, current: 50);

        var expiring = new TestHealthDeltaAffect(-5, masterCost: 1);
        var lasting = new TestHealthDeltaAffect(-3);
        article.ArticleStatus.ApplyAffectStatus(expiring);
        article.ArticleStatus.ApplyAffectStatus(lasting);

        article.ApplyTurnStartEffects();
        CheckEqual("H.BothAffectsAppliedOnFirstTurn", health.CurrentHealth, 42);
        CheckEqual("H.ExpiringAppliedOnce", expiring.ApplyCount, 1);
        CheckEqual("H.LastingAppliedOnce", lasting.ApplyCount, 1);

        // 만료된 효과는 목록에서 빠지고, 남은 효과만 다음 턴에 다시 적용된다.
        article.ApplyTurnStartEffects();
        CheckEqual("H.ExpiredAffectNotReapplied", expiring.ApplyCount, 1);
        CheckEqual("H.LastingAppliedTwice", lasting.ApplyCount, 2);
        CheckEqual("H.HealthAfterSecondTurn", health.CurrentHealth, 39);

        article.Free();
    }

    private static TProbe AddCustomTurnStartElement<TProbe>(CharacterArticle article, List<string> log)
        where TProbe : TurnStartProbe, new()
    {
        var probe = new TProbe { Log = log };
        article.ArticleStatus.StatusElementsDictionary[typeof(TProbe)] = probe;
        probe.Init(article);
        return probe;
    }

    private static TurnHelper MakeHelper(float speed, params RegenTurnArticle[] articles)
    {
        var helper = new TurnHelper();
        var articlesContainer = new ArticlesContainer();
        articlesContainer.Articles["Opponent"].Add(null);
        articlesContainer.Articles["Ally"].Add(null);
        var fxPlayer = new FxPlayer();
        helper.AddChild(articlesContainer);
        helper.AddChild(fxPlayer);
        SetPrivateField(helper, "_articlesContainer", articlesContainer);
        SetPrivateField(helper, "_fxPlayer", fxPlayer);
        SetPrivateField(helper, "_currentTurnArticle", null);

        var list = (List<ITurnAffectedArticle<ArticleBase>>)typeof(TurnHelper)
            .GetField("_turnAffectedArticleList", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(helper);
        list!.Clear();
        foreach (var article in articles) list.Add(article);

        typeof(TurnHelper).GetProperty(nameof(TurnHelper.Speed))?.SetValue(helper, speed);
        return helper;
    }

    private static void InvokeAdvanceToNextTurn(TurnHelper helper)
    {
        typeof(TurnHelper)
            .GetMethod("AdvanceToNextTurn", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(helper, Array.Empty<object>());
    }

    private static TestCharacterArticle MakeArticle(string name)
    {
        var article = new TestCharacterArticle { Name = name, ArticleStatus = new ArticleStatus() };
        var sprite = new AnimatedSprite2D { Name = "AnimatedSprite2D" };
        var animationPlayer = new AnimationPlayer { Name = "AnimationPlayer" };
        var overheadUi = MakeOverheadUi();
        sprite.AddChild(animationPlayer);
        article.AddChild(sprite);
        article.AddChild(overheadUi);
        SetPrivateField(article, "_animatedSprite2D", sprite);
        SetPrivateField(article, "_animationPlayer", animationPlayer);
        article.OverheadUi = overheadUi;
        return article;
    }

    // Health/Mana는 OverheadUi에 init_health/set_health/init_mana/set_mana를 Call한다. plain Control을 쓰면 매 회복마다
    // method-not-found 에러가 찍히므로, 그 네 메서드만 가진 test double 스크립트를 붙인 Control을 사용한다.
    private static Control MakeOverheadUi()
    {
        var script = GD.Load<GDScript>("res://Assets/Script/Tests/test_overhead_ui.gd");
        var overheadUi = (Control)script.New();
        overheadUi.Name = "OverheadUi";
        return overheadUi;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) continue;
            field.SetValue(target, value);
            return;
        }

        throw new MissingFieldException(target.GetType().Name, fieldName);
    }

    private static Health AddHealth(CharacterArticle article, int max, int current)
    {
        var health = new Health { MaxHealth = max };
        article.ArticleStatus.StatusElementsDictionary[typeof(Health)] = health;
        health.Init(article);
        health.CurrentHealth = current;
        return health;
    }

    private static Mana AddMana(CharacterArticle article, int max, int current)
    {
        var mana = new Mana { MaxMana = max };
        article.ArticleStatus.StatusElementsDictionary[typeof(Mana)] = mana;
        mana.Init(article);
        mana.CurrentMana = current;
        return mana;
    }

    private static HealthRegen AddHealthRegen(CharacterArticle article, int value)
    {
        var regen = new HealthRegen { Value = value };
        article.ArticleStatus.StatusElementsDictionary[typeof(HealthRegen)] = regen;
        regen.Init(article);
        return regen;
    }

    private static ManaRegen AddManaRegen(CharacterArticle article, int value)
    {
        var regen = new ManaRegen { Value = value };
        article.ArticleStatus.StatusElementsDictionary[typeof(ManaRegen)] = regen;
        regen.Init(article);
        return regen;
    }

    private static Health GetHealth(CharacterArticle article)
    {
        return article.ArticleStatus.StatusElementsDictionary[typeof(Health)] as Health;
    }

    private static Mana GetMana(CharacterArticle article)
    {
        return article.ArticleStatus.StatusElementsDictionary[typeof(Mana)] as Mana;
    }

    private partial class TestCharacterArticle : CharacterArticle { }

    // ArticleStatus를 고치지 않고 turn-start 스탯을 추가할 수 있는지 확인하는 테스트 전용 StatusElement.
    private abstract partial class TurnStartProbe : StatusElement, ITurnStartStatusElement
    {
        public List<string> Log { get; set; }
        public int ApplyCount { get; private set; }
        public int ObservedHealth { get; private set; } = -1;

        public abstract int TurnStartOrder { get; }
        protected abstract string LogName { get; }

        public void ApplyTurnStart(ArticleStatus status)
        {
            ApplyCount++;
            Log?.Add(LogName);
            if (status.TryGetStatusElement(out Health health)) ObservedHealth = health.CurrentHealth;
        }
    }

    private partial class EarlyTurnStartProbe : TurnStartProbe
    {
        public override int TurnStartOrder => 90;
        protected override string LogName => "early";
    }

    private partial class LateTurnStartProbe : TurnStartProbe
    {
        public override int TurnStartOrder => 200;
        protected override string LogName => "late";
    }

    // TurnHelper의 턴 리스트에 넣기 위한 wrapper. BehaviorTree 없이 실제 CharacterArticle의 턴 시작 hook만 실행한다.
    private sealed class RegenTurnArticle : ITurnAffectedArticle<ArticleBase>
    {
        private readonly BtStatus _status;

        public RegenTurnArticle(CharacterArticle article, BtStatus status)
        {
            Article = article;
            _status = status;
        }

        public CharacterArticle Article { get; }
        public int Priority { get; set; }
        public int SpawnIndex { get; set; }
        public TurnActionBase CurrentTurnAction { get; set; }
        public BehaviorTree BehaviorTree => null;
        public int TurnStartEffectCount { get; private set; }

        public void ApplyTurnStartEffects()
        {
            TurnStartEffectCount++;
            Article.ApplyTurnStartEffects();
        }

        public BtStatus TurnPlay(double delta) => _status;
    }

    // 턴 시작 지속 효과 fixture. IAffectedImmediately가 아니라 IAffectedOnlyMyTurn이어야
    // ApplyAffectStatus 시점이 아니라 ApplyAffectingStatuses() 시점에 적용된다.
    private sealed class TestHealthDeltaAffect : StatusAffect, IAffectedOnlyMyTurn
    {
        private readonly int _delta;
        private readonly int _masterCost;

        // masterCost 턴만큼 지속한다. 기본 99턴은 "이번 턴에 만료되지 않는" 효과를 뜻한다.
        public TestHealthDeltaAffect(int delta, int masterCost = 99)
        {
            _delta = delta;
            _masterCost = masterCost;
        }

        public int ApplyCount { get; private set; }
        public override HashSet<Type> AffectedType => new() { typeof(Health) };

        protected override int MasterCost => _masterCost;

        public void ApplyOnlyMyTurn<TStatus>(TStatus statusElement, ArticleStatus recipient) where TStatus : StatusElement
        {
            if (statusElement is not Health health) return;
            ApplyCount++;
            health.CurrentHealth += _delta;
        }
    }
}
#endif
