using System;
using UnityEngine;

namespace Project.Gameplay.AI
{
    public enum AIDifficulty
    {
        Easy = 0,
        Normal = 1,
        Hard = 2
    }

    /// <summary>Una sola IA base; la dificultad solo cambia parámetros (ticks, agresión, eficiencia).</summary>
    [Serializable]
    public sealed class AIDifficultyProfile
    {
        public float strategyTick = 6f;
        public float constructionTick = 1f;
        public float economyTick = 0.75f;
        public float militaryTick = 0.5f;
        public float tacticalTick = 0.35f;
        public float scoutingTick = 1.5f;
        public float reactionDelay = 0f;
        public float scoutingFrequency = 1f;
        [Range(0f, 2f)] public float aggression = 1f;
        [Range(0.25f, 1.5f)] public float economicEfficiency = 1f;
        [Range(0.25f, 1.5f)] public float tacticalSkill = 1f;
        public float minAttackAdvantage = 1.15f;
        public int maxBuilders = 2;

        public static AIDifficultyProfile Create(AIDifficulty d)
        {
            var p = new AIDifficultyProfile();
            switch (d)
            {
                case AIDifficulty.Easy:
                    p.strategyTick = 10f;
                    p.economyTick = 1.2f;
                    p.constructionTick = 1.6f;
                    p.militaryTick = 0.9f;
                    p.tacticalTick = 0.55f;
                    p.scoutingTick = 2.5f;
                    p.reactionDelay = 2.5f;
                    p.scoutingFrequency = 0.45f;
                    p.aggression = 0.45f;
                    p.economicEfficiency = 0.65f;
                    p.tacticalSkill = 0.55f;
                    p.minAttackAdvantage = 1.55f;
                    p.maxBuilders = 1;
                    break;
                case AIDifficulty.Hard:
                    p.strategyTick = 5f;
                    p.economyTick = 0.5f;
                    p.constructionTick = 0.85f;
                    p.militaryTick = 0.35f;
                    p.tacticalTick = 0.22f;
                    p.scoutingTick = 1f;
                    p.reactionDelay = 0.2f;
                    p.scoutingFrequency = 1.35f;
                    p.aggression = 1.35f;
                    p.economicEfficiency = 1.12f;
                    p.tacticalSkill = 1.2f;
                    p.minAttackAdvantage = 1.05f;
                    p.maxBuilders = 2;
                    break;
                default:
                    break;
            }
            return p;
        }
    }
}
