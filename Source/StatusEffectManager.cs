using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RPGStatusEffects
{
    public static class StatusEffectManager
    {
        private static readonly Dictionary<string, StatusEffect> CustomEffects = new Dictionary<string, StatusEffect>();

        public static void Initialize()
        {
            if (!RPGStatusEffects.IsObjectDBValid())
            {
                if (RPGStatusEffects.Instance.configVerboseLogging.Value)
                    Debug.LogWarning($"{RPGStatusEffects.PluginName}: Skipping StatusEffectManager init - ObjectDB not valid.");
                return;
            }
            CustomEffects.Clear(); // Clear to ensure fresh instances
            ObjectDB objectDB = ObjectDB.instance;
            RegisterPurityEffect(objectDB);
            RegisterTauntEffect(objectDB);
            RegisterTauntingEffect(objectDB); // New: Register player taunting effect
            if (RPGStatusEffects.Instance.configVerboseLogging.Value)
                Debug.Log($"{RPGStatusEffects.PluginName}: StatusEffectManager initialized {CustomEffects.Count} custom effects.");
        }

        private static void RegisterPurityEffect(ObjectDB objectDB)
        {
            const string effectName = "Purify";
            if (objectDB.m_StatusEffects.Any(se => se.name == effectName)) return;
            PurityEffect purityEffect = ScriptableObject.CreateInstance<PurityEffect>();
            purityEffect.name = effectName;
            purityEffect.Icon = LoadOrCreateIcon();
            purityEffect.Duration = RPGStatusEffects.Instance.configPurityDuration.Value;
            objectDB.m_StatusEffects.Add(purityEffect);
            CustomEffects[effectName] = purityEffect;
            if (RPGStatusEffects.Instance.configVerboseLogging.Value)
                Debug.Log($"{RPGStatusEffects.PluginName}: Registered PurityEffect with duration {purityEffect.Duration}s, Icon: {(purityEffect.Icon != null ? "Assigned" : "Null")}.");
        }

        private static void RegisterTauntEffect(ObjectDB objectDB)
        {
            const string effectName = "Taunted";
            // Remove existing effect to ensure duration updates
            var existingEffect = objectDB.m_StatusEffects.FirstOrDefault(se => se.name == effectName);
            if (existingEffect != null)
            {
                objectDB.m_StatusEffects.Remove(existingEffect);
                if (RPGStatusEffects.Instance.configVerboseLogging.Value)
                    Debug.Log($"{RPGStatusEffects.PluginName}: Removed existing Taunted effect to update duration.");
            }
            TauntEffect tauntEffect = ScriptableObject.CreateInstance<TauntEffect>();
            tauntEffect.name = effectName;
            tauntEffect.Duration = RPGStatusEffects.Instance.configTauntDuration.Value;
            tauntEffect.Icon = LoadTauntIcon();
            objectDB.m_StatusEffects.Add(tauntEffect);
            CustomEffects[effectName] = tauntEffect;
            if (RPGStatusEffects.Instance.configVerboseLogging.Value)
                Debug.Log($"{RPGStatusEffects.PluginName}: Registered TauntEffect with duration {tauntEffect.Duration}s, Icon: {(tauntEffect.Icon != null ? "Assigned" : "Null")}.");
        }

        private static void RegisterTauntingEffect(ObjectDB objectDB)
        {
            const string effectName = "Taunting";
            // Remove existing effect to ensure duration updates
            var existingEffect = objectDB.m_StatusEffects.FirstOrDefault(se => se.name == effectName);
            if (existingEffect != null)
            {
                objectDB.m_StatusEffects.Remove(existingEffect);
                if (RPGStatusEffects.Instance.configVerboseLogging.Value)
                    Debug.Log($"{RPGStatusEffects.PluginName}: Removed existing Taunting effect to update duration.");
            }
            TauntingEffect tauntingEffect = ScriptableObject.CreateInstance<TauntingEffect>();
            tauntingEffect.name = effectName;
            tauntingEffect.Duration = RPGStatusEffects.Instance.configTauntDuration.Value;
            tauntingEffect.Icon = LoadTauntingIcon();
            objectDB.m_StatusEffects.Add(tauntingEffect);
            CustomEffects[effectName] = tauntingEffect;
            if (RPGStatusEffects.Instance.configVerboseLogging.Value)
                Debug.Log($"{RPGStatusEffects.PluginName}: Registered TauntingEffect with duration {tauntingEffect.Duration}s, Icon: {(tauntingEffect.Icon != null ? "Assigned" : "Null")}.");
        }

        private static Sprite LoadOrCreateIcon()
        {
            Texture2D fallbackTexture = new Texture2D(64, 64);
            Color[] pixels = new Color[64 * 64];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.cyan;
            }
            fallbackTexture.SetPixels(pixels);
            fallbackTexture.Apply();
            return Sprite.Create(fallbackTexture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
        }

        private static Sprite LoadTauntIcon()
        {
            Texture2D fallbackTexture = new Texture2D(64, 64);
            Color[] pixels = new Color[64 * 64];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.red;
            }
            fallbackTexture.SetPixels(pixels);
            fallbackTexture.Apply();
            return Sprite.Create(fallbackTexture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
        }

        private static Sprite LoadTauntingIcon()
        {
            Texture2D fallbackTexture = new Texture2D(64, 64);
            Color[] pixels = new Color[64 * 64];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.yellow; // Yellow for player taunting theme
            }
            fallbackTexture.SetPixels(pixels);
            fallbackTexture.Apply();
            return Sprite.Create(fallbackTexture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
        }

        public static StatusEffect GetEffect(string name)
        {
            CustomEffects.TryGetValue(name, out var effect);
            return effect;
        }
    }
}