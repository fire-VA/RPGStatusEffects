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
            RegisterTauntingEffect(objectDB);
            if (RPGStatusEffects.Instance.configVerboseLogging.Value)
                Debug.Log($"{RPGStatusEffects.PluginName}: StatusEffectManager initialized {CustomEffects.Count} custom effects.");
        }

        private static void RegisterPurityEffect(ObjectDB objectDB)
        {
            const string effectName = "Purify";
            // Remove existing effect to ensure duration updates
            var existingEffect = objectDB.m_StatusEffects.FirstOrDefault(se => se.name == effectName);
            if (existingEffect != null)
            {
                objectDB.m_StatusEffects.Remove(existingEffect);
                if (RPGStatusEffects.Instance.configVerboseLogging.Value)
                    Debug.Log($"{RPGStatusEffects.PluginName}: Removed existing Purify effect to update duration.");
            }
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
            tauntEffect.Icon = null; // No icon for enemy-side Taunted effect (not shown on HUD)
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

        private static Sprite LoadOrCreateIcon() // For Purity
        {
            string[] possiblePaths = new[]
            {
                "assets/custom/vaitems/icons/purity_icon",
                "Assets/Custom/VAItems/Icons/purity_icon",
                "assets/custom/VAitems/icons/purity_icon",
                "Assets/Custom/VAItems/Icons/Purity_Icon",
                "assets/custom/vaitems/icons/purity_icon.png"
            };
            Sprite puritySprite = null;
            foreach (var path in possiblePaths)
            {
                puritySprite = RPGStatusEffects.assetBundle?.LoadAsset<Sprite>(path);
                if (puritySprite != null)
                {
                    if (RPGStatusEffects.Instance.configVerboseLogging.Value)
                        Debug.Log($"{RPGStatusEffects.PluginName}: Loaded purity_icon from asset bundle at {path}.");
                    break;
                }
            }
            if (puritySprite == null)
            {
                if (RPGStatusEffects.Instance.configVerboseLogging.Value)
                {
                    Debug.LogWarning($"{RPGStatusEffects.PluginName}: Failed to load purity_icon from asset bundle. Attempted paths: {string.Join(", ", possiblePaths)}.");
                    if (RPGStatusEffects.assetBundle != null)
                        Debug.Log($"{RPGStatusEffects.PluginName}: Available assets in bundle: {string.Join(", ", RPGStatusEffects.assetBundle.GetAllAssetNames())}.");
                    Debug.Log($"{RPGStatusEffects.PluginName}: Falling back to cyan square for Purity icon.");
                }
                Texture2D fallbackTexture = new Texture2D(64, 64);
                Color[] pixels = new Color[64 * 64];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = Color.cyan;
                }
                fallbackTexture.SetPixels(pixels);
                fallbackTexture.Apply();
                puritySprite = Sprite.Create(fallbackTexture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
            }
            return puritySprite;
        }

        private static Sprite LoadTauntIcon() // For Taunted (enemy, not shown)
        {
            return null; // No icon for enemy-side Taunted effect
        }

        private static Sprite LoadTauntingIcon() // For Taunting (player)
        {
            string[] possiblePaths = new[]
            {
                "assets/custom/vaitems/icons/taunt_icon",
                "Assets/Custom/VAItems/Icons/taunt_icon",
                "assets/custom/VAitems/icons/taunt_icon",
                "Assets/Custom/VAItems/Icons/Taunt_Icon",
                "assets/custom/vaitems/icons/taunt_icon.png"
            };
            Sprite tauntSprite = null;
            foreach (var path in possiblePaths)
            {
                tauntSprite = RPGStatusEffects.assetBundle?.LoadAsset<Sprite>(path);
                if (tauntSprite != null)
                {
                    if (RPGStatusEffects.Instance.configVerboseLogging.Value)
                        Debug.Log($"{RPGStatusEffects.PluginName}: Loaded taunt_icon from asset bundle at {path}.");
                    break;
                }
            }
            if (tauntSprite == null)
            {
                if (RPGStatusEffects.Instance.configVerboseLogging.Value)
                {
                    Debug.LogWarning($"{RPGStatusEffects.PluginName}: Failed to load taunt_icon from asset bundle. Attempted paths: {string.Join(", ", possiblePaths)}.");
                    if (RPGStatusEffects.assetBundle != null)
                        Debug.Log($"{RPGStatusEffects.PluginName}: Available assets in bundle: {string.Join(", ", RPGStatusEffects.assetBundle.GetAllAssetNames())}.");
                    Debug.Log($"{RPGStatusEffects.PluginName}: Falling back to yellow square for Taunting icon.");
                }
                Texture2D fallbackTexture = new Texture2D(64, 64);
                Color[] pixels = new Color[64 * 64];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = Color.yellow;
                }
                fallbackTexture.SetPixels(pixels);
                fallbackTexture.Apply();
                tauntSprite = Sprite.Create(fallbackTexture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
            }
            return tauntSprite;
        }

        public static StatusEffect GetEffect(string name)
        {
            CustomEffects.TryGetValue(name, out var effect);
            return effect;
        }
    }
}