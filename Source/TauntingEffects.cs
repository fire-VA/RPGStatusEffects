using UnityEngine;

namespace RPGStatusEffects
{
    public class TauntingEffect : StatusEffect
    {
        public float Duration { get; set; } = 15f;
        public Sprite Icon { get; set; }

        public override void Setup(Character character)
        {
            base.Setup(character);
            m_ttl = Duration;
            m_name = "Taunting";
            m_icon = Icon;
            if (m_character != null && m_character.IsPlayer())
            {
                m_character.Message(MessageHud.MessageType.TopLeft, "Taunting enemy!");
                if (RPGStatusEffects.Instance.configVerboseLogging.Value)
                    Debug.Log($"{RPGStatusEffects.PluginName}: Setup TauntingEffect on player {m_character.name}, Duration: {m_ttl}s, Icon: {(m_icon != null ? "Assigned" : "Null")}.");
            }
        }

        public override void UpdateStatusEffect(float dt)
        {
            base.UpdateStatusEffect(dt);
        }

        public override void Stop()
        {
            base.Stop();
            if (m_character != null && m_character.IsPlayer())
            {
                m_character.Message(MessageHud.MessageType.TopLeft, "Taunt expired.");
            }
        }
    }
}