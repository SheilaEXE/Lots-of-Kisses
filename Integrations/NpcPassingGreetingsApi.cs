namespace LotsOfKisses
{
    public interface INpcPassingGreetingsApi
    {
        bool ShouldSuppressBumpKissDialogue(string npcName);

        double GetBumpKissDialogueSilenceRemainingMs(string npcName);
    }
}