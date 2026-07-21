namespace LotsOfKisses
{
    internal enum PlayerKissMode
    {
        Simple = 0,
        Bump = 1,
        Multi = 2,
        SingleTier = 3
    }

    internal sealed class PlayerKissRequestMessage
    {
        public string RequestId { get; set; } = "";
        public long InitiatorId { get; set; }
        public long TargetId { get; set; }
        public string LocationName { get; set; } = "";
        public PlayerKissMode Mode { get; set; }
        public int Tier { get; set; }
    }

    internal sealed class PlayerKissAcceptMessage
    {
        public string RequestId { get; set; } = "";
        public long InitiatorId { get; set; }
        public long TargetId { get; set; }
    }

    internal sealed class PlayerKissCycleMessage
    {
        public string SequenceId { get; set; } = "";
        public long InitiatorId { get; set; }
        public long TargetId { get; set; }
        public string LocationName { get; set; } = "";
        public PlayerKissMode Mode { get; set; }
        public int Tier { get; set; }
        public int CycleNumber { get; set; }
    }

    internal sealed class PlayerKissStopMessage
    {
        public string SequenceId { get; set; } = "";
        public long InitiatorId { get; set; }
        public long TargetId { get; set; }
    }
}
