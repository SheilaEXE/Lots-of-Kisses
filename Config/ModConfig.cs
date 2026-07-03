namespace LotsOfKisses
{
    public enum BlushSmokeStyle
    {
        Style1 = 0,
        Style2 = 1
    }

    public class ModConfig
    {
        // Ativar/Desativar o Mod
        public bool AtivarMod { get; set; } = true;

        // Chance of receiving a gift from your partner on a bump kiss — fixed at 5%, not exposed in GMCM.
        public int ChancePresenteEsbarrao { get; set; } = 5;

        // Enable or disable multi-kiss sequences
        public bool AtivarTrocaDeBeijos { get; set; } = true;

        // Ligar/Desligar o beijo ao esbarrar
        public bool AtivarBeijoEsbarrao { get; set; } = true;

        // Dating partners are always supported — not exposed in GMCM, always true.
        public bool AtivarNamorados { get; set; } = true;

        // Enables generic polyamory mod support by treating all married NPCs in friendshipData as valid romantic partners.
        public bool AtivarCompatibilidadePoliamor { get; set; } = true;

        // Which blush smoke animation style to use (row 0 = Style1, row 1 = Style2).
        public BlushSmokeStyle EstiloBlushSmoke { get; set; } = BlushSmokeStyle.Style2;
    }
}