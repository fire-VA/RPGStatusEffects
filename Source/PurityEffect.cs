using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace RPGStatusEffects
{
    public class PurityEffect : StatusEffect
    {
        private static readonly HashSet<string> DebuffNames = new HashSet<string>
        {
            "Poison",
            "Burning",
            "Frost",
            "Wet",
            "Smoked",
            "Tared",
        };

        public Sprite Icon { get; set; }
        public float Duration { get; set; } = 10f;

        public override void Setup(Character character)
        {
            base.Setup(character);
            m_ttl = Duration;
            m_name = "Purify";
            m_icon = Icon;
            if (m_character is Player player)
            {
                List<StatusEffect> effects = new List<StatusEffect>(player.GetSEMan().GetStatusEffects());
                foreach (var effect in effects)
                {
                    if (effect != this && DebuffNames.Contains(effect.name))
                    {
                        player.GetSEMan().RemoveStatusEffect(effect, true);
                    }
                }
                player.Message(MessageHud.MessageType.Center, "All debuffs purified!");
            }
        }
    }
}