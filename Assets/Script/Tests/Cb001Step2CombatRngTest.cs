#if TOOLS
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AutoCrawler.Assets.Script.Article.Status;
using AutoCrawler.Assets.Script.Article.Status.Affect;
using AutoCrawler.Assets.Script.Article.Status.Element;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

public partial class Cb001Step2CombatRngTest : Node
{
    private int _failures;

    public override void _Ready()
    {
        if (OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")
        {
            int failures = RunTests();
            if (failures == 0)
            {
                GD.Print("[CB-001 Step2] ALL PASS");
                GetTree().Quit(0);
            }
            else
            {
                GD.Print($"[CB-001 Step2] FAILED: {failures} assertion(s)");
                GetTree().Quit(1);
            }
        }
    }

    public int RunTests()
    {
        GD.Print("[CB-001 Step2] Running CombatRng tests...");
        try
        {
            TestTurnHelperCombatRngSameSeedSequence();
            TestPhysicalDamageSameSeedSequence();
            TestStaticGuardForForbiddenCombatRandomApis();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[CB-001 Step2] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            SetBattleFieldScene(null);
        }

        return _failures;
    }

    private void Check(string name, bool actual, bool expected)
    {
        if (actual == expected)
        {
            GD.Print($"  PASS: {name}");
        }
        else
        {
            _failures++;
            GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}");
        }
    }

    private void CheckEqual<T>(string name, T actual, T expected)
    {
        if (EqualityComparer<T>.Default.Equals(actual, expected))
        {
            GD.Print($"  PASS: {name}");
        }
        else
        {
            _failures++;
            GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}");
        }
    }

    private void TestTurnHelperCombatRngSameSeedSequence()
    {
        GD.Print("[A] TurnHelper CombatRng same seed sequence");
        var helper = new TurnHelper();

        helper.ResetCombatRng(424242);
        var first = new List<string>();
        for (int i = 0; i < 8; i++)
        {
            first.Add($"{helper.CombatRandRange(0.0, 100.0):F6}:{helper.CombatRandRange(1, 20)}");
        }

        helper.ResetCombatRng(424242);
        var second = new List<string>();
        for (int i = 0; i < 8; i++)
        {
            second.Add($"{helper.CombatRandRange(0.0, 100.0):F6}:{helper.CombatRandRange(1, 20)}");
        }

        CheckEqual("A.sequence", string.Join("|", first), string.Join("|", second));
        helper.Free();
    }

    private void TestPhysicalDamageSameSeedSequence()
    {
        GD.Print("[B] PhysicalDamage same seed sequence");
        var helper = new TurnHelper();
        var battleField = new BattleFieldScene();
        SetPrivateField(battleField, "_turnHelper", helper);
        SetBattleFieldScene(battleField);

        string first = MakePhysicalDamageSequence(helper, 987654);
        string second = MakePhysicalDamageSequence(helper, 987654);

        CheckEqual("B.damage_sequence", first, second);

        battleField.Free();
        helper.Free();
        SetBattleFieldScene(null);
    }

    private static string MakePhysicalDamageSequence(TurnHelper helper, long seed)
    {
        helper.ResetCombatRng(seed);
        var giver = MakeStatus(strength: 7, luck: 250, defense: 0);
        var recipient = MakeStatus(strength: 0, luck: 0, defense: 3);

        var values = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            var damage = Damage.CreateDamage<PhysicalDamage>(giver, 12, 24);
            int calculated = InvokeCalculatedDamage(damage, recipient);
            bool critical = ReadIsCritical(damage);
            values.Add($"{critical}:{calculated}");
        }

        return string.Join("|", values);
    }

    private void TestStaticGuardForForbiddenCombatRandomApis()
    {
        GD.Print("[C] Static guard forbidden random APIs");
        string root = ProjectSettings.GlobalizePath("res://Assets/Script");
        string[] forbiddenPatterns =
        {
            "GD." + "Rand",
            "new " + "Random(",
            "new System." + "Random(",
            "Random." + "Shared",
            "Guid." + "NewGuid"
        };

        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("/Assets/Script/Tests/")) continue;

            string content = File.ReadAllText(file);
            foreach (string pattern in forbiddenPatterns)
            {
                if (content.Contains(pattern, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(root, file).Replace('\\', '/')}:{pattern}");
                }
            }
        }

        if (offenders.Count > 0)
        {
            GD.Print("  FAIL: C.static_guard offenders -> " + string.Join(", ", offenders));
            _failures++;
            return;
        }

        GD.Print("  PASS: C.static_guard");
    }

    private static ArticleStatus MakeStatus(int strength, int luck, int defense)
    {
        var status = new ArticleStatus();
        status.StatusElementsDictionary[typeof(Strength)] = new Strength { Value = strength };
        status.StatusElementsDictionary[typeof(Luck)] = new Luck { Value = luck };
        status.StatusElementsDictionary[typeof(Defense)] = new Defense { Value = defense };
        return status;
    }

    private static int InvokeCalculatedDamage(PhysicalDamage damage, ArticleStatus recipient)
    {
        var method = typeof(PhysicalDamage).GetMethod("CalculatedDamage", BindingFlags.NonPublic | BindingFlags.Instance);
        return (int)method.Invoke(damage, new object[] { recipient });
    }

    private static bool ReadIsCritical(PhysicalDamage damage)
    {
        var property = typeof(PhysicalDamage).GetProperty("IsCritical", BindingFlags.NonPublic | BindingFlags.Instance);
        return (bool)property.GetValue(damage);
    }

    private static void SetBattleFieldScene(BattleFieldScene battleField)
    {
        typeof(BattleFieldScene)
            .GetField("_battleFieldScene", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, battleField);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        target.GetType()
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?.SetValue(target, value);
    }
}
#endif