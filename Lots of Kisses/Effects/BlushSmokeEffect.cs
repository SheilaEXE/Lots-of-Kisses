using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System;
using System.Collections.Generic;

namespace LotsOfKisses
{
    public partial class ModEntry
    {
        private Texture2D blushSmokeTexture;

        private readonly Dictionary<string, BlushSmokeInstance> activeBlushSmokes = new();

        private const int BlushSmokeFrameWidth = 16;
        private const int BlushSmokeFrameHeight = 16;

        // Lower = faster.
        // 4 ticks = very fast.
        // 6 ticks = fast but easier to see.
        // 8 ticks = closer to a slower animation.
        private const int BlushSmokeTicksPerFrame = 8;

        private const float BlushSmokeScale = 4f;

        private static readonly int[] BlushSmokeFrameSequence = { 0, 1, 2, 3, 4, 0, 1, 2, 3, 4 };
        private sealed class BlushSmokeInstance
        {
            public NPC Npc;
            public int Row;
            public int SequenceIndex;
            public int Timer;
        }

        private void InitBlushSmokeEffect()
        {
            blushSmokeTexture = Helper.ModContent.Load<Texture2D>("assets/blushes.png");

            Helper.Events.GameLoop.UpdateTicked += OnBlushSmokeUpdateTicked;
            Helper.Events.Display.RenderedWorld += OnBlushSmokeRenderedWorld;
        }

        private void ShowBlushSmoke(NPC npc, int smokeId = 64)
        {
            if (npc == null)
                return;

            int row = (int)(this.Config?.EstiloBlushSmoke ?? BlushSmokeStyle.Style2);

            activeBlushSmokes[npc.Name] = new BlushSmokeInstance
            {
                Npc = npc,
                Row = row,
                SequenceIndex = 0,
                Timer = 0
            };
        }
        private void OnBlushSmokeUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady)
            {
                activeBlushSmokes.Clear();
                return;
            }

            List<string> finished = new();

            foreach (var pair in activeBlushSmokes)
            {
                BlushSmokeInstance smoke = pair.Value;

                if (smoke.Npc == null)
                {
                    finished.Add(pair.Key);
                    continue;
                }

                smoke.Timer++;

                if (smoke.Timer >= BlushSmokeTicksPerFrame)
                {
                    smoke.Timer = 0;
                    smoke.SequenceIndex++;

                    if (smoke.SequenceIndex >= BlushSmokeFrameSequence.Length)
                        finished.Add(pair.Key);
                }
            }

            foreach (string key in finished)
                activeBlushSmokes.Remove(key);
        }

        private void OnBlushSmokeRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            if (!Context.IsWorldReady || blushSmokeTexture == null)
                return;

            foreach (BlushSmokeInstance smoke in activeBlushSmokes.Values)
            {
                NPC npc = smoke.Npc;

                if (npc == null || npc.currentLocation != Game1.currentLocation)
                    continue;

                int frame = BlushSmokeFrameSequence[Math.Min(smoke.SequenceIndex, BlushSmokeFrameSequence.Length - 1)];

                Rectangle sourceRect = new Rectangle(
                    frame * BlushSmokeFrameWidth,
                    smoke.Row * BlushSmokeFrameHeight,
                    BlushSmokeFrameWidth,
                    BlushSmokeFrameHeight
                );

                // Position above the head.
                // Ajuste o -104f se quiser mais alto/baixo.
                Vector2 worldPos = npc.Position + new Vector2(32f, -80f);

                // Drifts upward slightly during the animation to look like rising smoke.
                worldPos.Y -= frame * 2f;

                Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos);

                // Origem no meio inferior do frame 16x16.
                Vector2 origin = new Vector2(8f, 16f);

                float layerDepth = Math.Min(0.999f, (npc.Position.Y + 128f) / 10000f);

                e.SpriteBatch.Draw(
                    blushSmokeTexture,
                    screenPos,
                    sourceRect,
                    Color.White,
                    0f,
                    origin,
                    BlushSmokeScale,
                    SpriteEffects.None,
                    layerDepth
                );
            }
        }
    }
}