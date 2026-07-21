using Microsoft.Xna.Framework;

namespace LotsOfKisses
{
    internal sealed class PlayerKissState
    {
        public string OutgoingRequestId { get; set; } = "";
        public long OutgoingTargetId { get; set; }
        public int OutgoingRequestTicksRemaining { get; set; }
        public PlayerKissMode OutgoingMode { get; set; }
        public int OutgoingTier { get; set; }

        public int CooldownTicksRemaining { get; set; }
        public int BumpCooldownTicksRemaining { get; set; }
        public bool WasTouchingSpouse { get; set; }
        public int AutomaticMultiKissHoldTicks { get; set; }

        public string SequenceId { get; set; } = "";
        public long InitiatorId { get; set; }
        public long TargetId { get; set; }
        public PlayerKissMode Mode { get; set; }
        public int Tier { get; set; }
        public int CycleNumber { get; set; }
        public int CycleTimerTicks { get; set; }
        public int GapTimerTicks { get; set; }
        public int FollowerSafetyTicks { get; set; }
        public bool IsAuthority { get; set; }
        public bool IsCycleActive { get; set; }
        public bool HeartTriggered { get; set; }
        public bool SmokeTriggered { get; set; }

        public bool IsApplyingAnimation { get; set; }
        public int ApplyingAnimationDurationMs { get; set; }
        public bool ApplyingContinuousAnimation { get; set; }

        public int LeanPhase { get; set; }
        public int LeanTimerTicks { get; set; }
        public Vector2 LeanDirection { get; set; }
        public Vector2 LeanAppliedOffset { get; set; }

        public bool HasOutgoingRequest => !string.IsNullOrEmpty(OutgoingRequestId);
        public bool HasActiveSequence => !string.IsNullOrEmpty(SequenceId);

        public void ClearOutgoingRequest()
        {
            OutgoingRequestId = "";
            OutgoingTargetId = 0;
            OutgoingRequestTicksRemaining = 0;
            OutgoingMode = PlayerKissMode.Simple;
            OutgoingTier = 1;
        }

        public void ClearActiveSequence()
        {
            SequenceId = "";
            InitiatorId = 0;
            TargetId = 0;
            Mode = PlayerKissMode.Simple;
            Tier = 1;
            CycleNumber = 0;
            CycleTimerTicks = 0;
            GapTimerTicks = 0;
            FollowerSafetyTicks = 0;
            IsAuthority = false;
            IsCycleActive = false;
            HeartTriggered = false;
            SmokeTriggered = false;
            IsApplyingAnimation = false;
            ApplyingAnimationDurationMs = 0;
            ApplyingContinuousAnimation = false;
            LeanPhase = 0;
            LeanTimerTicks = 0;
            LeanDirection = Vector2.Zero;
            LeanAppliedOffset = Vector2.Zero;
        }

        public void Reset()
        {
            ClearOutgoingRequest();
            ClearActiveSequence();
            CooldownTicksRemaining = 0;
            BumpCooldownTicksRemaining = 0;
            WasTouchingSpouse = false;
            AutomaticMultiKissHoldTicks = 0;
        }
    }
}
