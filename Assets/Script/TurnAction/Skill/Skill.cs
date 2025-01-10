namespace AutoCrawler.Assets.Script.TurnAction.Skill;

public interface ISkill<T> where T : TurnActionBase 
{
    public int Distance { get; }
    public int Range { get; }
}