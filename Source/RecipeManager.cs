using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RPGStatusEffects
{
    public static class RecipeManager
    {
        private static readonly Dictionary<string, Recipe> _existingRecipes = new();
        private static readonly Dictionary<string, Piece.Requirement[]> itemRecipeCache = new();

        public static void SetupRecipe()
        {
            if (ObjectDB.instance == null || ObjectDB.instance.m_recipes == null || ZNetScene.instance == null) return;
            _existingRecipes.Clear();
            string itemName = "TauntHammer_vad";
            GameObject itemPrefab = RPGStatusEffects.itemPrefabs.FirstOrDefault(p => p != null && string.Equals(p.name, itemName, System.StringComparison.OrdinalIgnoreCase));
            if (itemPrefab == null) return;
            var itemDrop = itemPrefab.GetComponent<ItemDrop>();
            if (itemDrop == null) return;

            // Assign only the Taunt effect
            TauntEffect tauntEffect = StatusEffectManager.GetEffect("Taunted") as TauntEffect;
            if (tauntEffect != null)
            {
                itemDrop.m_itemData.m_shared.m_attackStatusEffect = tauntEffect;
                itemDrop.m_itemData.m_shared.m_attackStatusEffectChance = 1f;
                if (RPGStatusEffects.Instance.configVerboseLogging.Value)
                    Debug.Log($"{RPGStatusEffects.PluginName}: Assigned Taunt effect to TauntHammer_vad (effect: {tauntEffect.name}, duration: {tauntEffect.Duration}s).");
            }

            var station = ZNetScene.instance.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
            if (station == null) return;
            var configValue = RPGStatusEffects.Instance.configTauntHammerRecipe.Value;
            var requirements = ParseRequirements(configValue, itemName);
            if (requirements.Length == 0) return;

            var recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = "Recipe_" + itemName;
            recipe.m_item = itemDrop;
            recipe.m_craftingStation = station;
            recipe.m_minStationLevel = 1;
            recipe.m_amount = 1;
            recipe.m_resources = requirements;

            if (!ObjectDB.instance.m_recipes.Any(r => r.name == recipe.name))
            {
                ObjectDB.instance.m_recipes.Add(recipe);
                _existingRecipes[itemName] = recipe;
                if (RPGStatusEffects.Instance.configVerboseLogging.Value)
                    Debug.Log($"{RPGStatusEffects.PluginName}: Added Recipe_TauntHammer_vad to ObjectDB.m_recipes");
            }

            // Add to player's known recipes
            if (Player.m_localPlayer != null)
            {
                var knownRecipes = (HashSet<string>)RPGStatusEffects.knownRecipesField?.GetValue(Player.m_localPlayer);
                if (knownRecipes != null && !knownRecipes.Contains(recipe.name))
                {
                    knownRecipes.Add(recipe.name);
                    if (RPGStatusEffects.Instance.configVerboseLogging.Value)
                        Debug.Log($"{RPGStatusEffects.PluginName}: Added Recipe_TauntHammer_vad to player's known recipes.");
                }
            }
        }

        public static void ClearCache()
        {
            itemRecipeCache.Clear();
        }

        private static Piece.Requirement[] ParseRequirements(string configValue, string key)
        {
            if (itemRecipeCache.TryGetValue(key, out var cachedReqs))
                return cachedReqs;

            var reqs = new List<Piece.Requirement>();
            var parts = configValue.Split(',').Select(s => s.Trim()).ToArray();
            for (int i = 0; i < parts.Length; i += 3)
            {
                if (i + 2 >= parts.Length) break;
                string name = parts[i];
                if (!int.TryParse(parts[i + 1], out int amount)) amount = 1;
                if (!int.TryParse(parts[i + 2], out int perLevel)) perLevel = 0;

                var item = ZNetScene.instance?.GetPrefab(name)?.GetComponent<ItemDrop>()
                           ?? ObjectDB.instance?.GetItemPrefab(name)?.GetComponent<ItemDrop>();
                if (item == null)
                {
                    if (RPGStatusEffects.Instance.configVerboseLogging.Value)
                        Debug.LogWarning($"{RPGStatusEffects.PluginName}: Invalid material '{name}' in recipe for {key} - skipping.");
                    continue;
                }

                reqs.Add(new Piece.Requirement
                {
                    m_resItem = item,
                    m_amount = amount,
                    m_amountPerLevel = perLevel,
                    m_recover = true
                });
            }

            var reqsArray = reqs.ToArray();
            itemRecipeCache[key] = reqsArray;
            if (reqsArray.Length == 0 && RPGStatusEffects.Instance.configVerboseLogging.Value)
                Debug.LogWarning($"{RPGStatusEffects.PluginName}: No valid requirements parsed for {key} - recipe may fail.");
            return reqsArray;
        }
    }
}