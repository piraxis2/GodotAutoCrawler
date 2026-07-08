using AutoCrawler.Assets.Script.Article;
using Godot;

namespace AutoCrawler.Assets.Script.SkillSystem;

public sealed class SkillContext
{
    public SkillContext(ArticleBase caster, BattleFieldTileMapLayer tileMapLayer, FxPlayer fxPlayer, TurnHelper turnHelper)
    {
        Caster = caster;
        TileMapLayer = tileMapLayer;
        FxPlayer = fxPlayer;
        TurnHelper = turnHelper;
    }

    public ArticleBase Caster { get; }
    public BattleFieldTileMapLayer TileMapLayer { get; }
    public FxPlayer FxPlayer { get; }
    public TurnHelper TurnHelper { get; }

    public static SkillContext FromBattleField(ArticleBase caster)
    {
        var battleField = BattleFieldScene.BattleField;
        return new SkillContext(caster, battleField?.BattleFieldTileMap, battleField?.FxPlayer, battleField?.TurnHelper);
    }

    public double CombatRandRange(double min, double max)
    {
        if (TurnHelper == null)
        {
            throw new System.InvalidOperationException("SkillContext requires TurnHelper for combat RNG.");
        }

        return TurnHelper.CombatRandRange(min, max);
    }

    public int CombatRandRange(int min, int max)
    {
        if (TurnHelper == null)
        {
            throw new System.InvalidOperationException("SkillContext requires TurnHelper for combat RNG.");
        }

        return TurnHelper.CombatRandRange(min, max);
    }
}