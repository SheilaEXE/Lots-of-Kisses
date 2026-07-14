using Microsoft.Xna.Framework.Audio;
using StardewModdingAPI;
using System;
using System.IO;

namespace LotsOfKisses
{
    public partial class ModEntry
    {
        private SoundEffect? hugSound;

        private void InitCustomSounds()
        {
            try
            {
                string path = Path.Combine(Helper.DirectoryPath, "assets", "sounds", "hug.wav");

                if (!File.Exists(path))
                {
                    Monitor.Log($"Sound hug.wav not found at: {path}", LogLevel.Warn);
                    return;
                }

                using FileStream stream = File.OpenRead(path);
                hugSound = SoundEffect.FromStream(stream);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error loading hug.wav: {ex}", LogLevel.Warn);
            }
        }

        private void PlayHugSound()
        {
            try
            {
                if (hugSound == null)
                    return;

                // volume, pitch, pan
                hugSound.Play(0.75f, 0f, 0f);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error playing hug.wav: {ex}", LogLevel.Trace);
            }
        }
    }
}
