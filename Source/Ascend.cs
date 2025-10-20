using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RPGStatusEffects
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class RPGStatusEffects : BaseUnityPlugin
    {
        public static RPGStatusEffects Instance;
        private readonly Harmony harmony = new Harmony(PluginGUID);
        public const string PluginGUID = "com.Fire.rpgstatuseffects";
        public const string PluginName = "RPGStatusEffects";
        public const string PluginVersion = "1.0.1"; // Updated to 1.0.1

        public ConfigSync configSync;
        public SyncedConfigEntry<bool> configVerboseLogging;
        public SyncedConfigEntry<float> configPurityDuration;
        public SyncedConfigEntry<float> configTauntDuration;
        public SyncedConfigEntry<string> configTauntHammerRecipe;
        private bool isInitialized;
        private bool isShuttingDown;
        public static AssetBundle assetBundle; // Changed to public
        private Dictionary<string, Piece.Requirement[]> itemRecipeCache = new Dictionary<string, Piece.Requirement[]>();
        private FieldInfo knownRecipesField;
        private static readonly Dictionary<Character, float> lastTauntLogTimes = new Dictionary<Character, float>();
        private static readonly Dictionary<Character, (bool fleeIfHurt, bool fleeIfLowHealth, bool enableHuntPlayer)> originalHuntStates = new Dictionary<Character, (bool, bool, bool)>();

        private void Awake()
        {
            Instance = this;
            knownRecipesField = typeof(Player).GetField("m_knownRecipes", BindingFlags.NonPublic | BindingFlags.Instance);
            configSync = new ConfigSync(PluginGUID) { DisplayName = PluginName, CurrentVersion = PluginVersion, MinimumRequiredVersion = PluginVersion };
            configSync.lockedConfigChanged += () =>
            {
                if (isShuttingDown || !isInitialized || !IsObjectDBValid()) return;
                ValidateConfigs();
                SetupStatusEffects();
                AddTauntHammer();
                if (configVerboseLogging.Value)
                    Debug.Log($"{PluginName}: Config sync triggered status effect and item update.");
            };

            configVerboseLogging = configSync.AddConfigEntry(Config.Bind("General", "VerboseLogging", false, "Enable detailed debug logs."));
            configPurityDuration = configSync.AddConfigEntry(Config.Bind("StatusEffects", "PurityDuration", 10f, "Duration of the Purity status effect in seconds."));
            configTauntDuration = configSync.AddConfigEntry(Config.Bind("StatusEffects", "TauntDuration", 15f, "Duration of the Taunt effect in seconds."));
            configTauntHammerRecipe = configSync.AddConfigEntry(Config.Bind("Item_Recipe_TauntHammer", "Recipe", "Wood,10,5,LeatherScraps,5,2,SwordCheat,1,0", "Recipe for TauntHammer_vad (format: ItemName,Amount,AmountPerLevel,...)"));

            ValidateConfigs();
            loadAssets();
            harmony.PatchAll();
            RegisterConsoleCommands();
            StartCoroutine(InitializeStatusEffects());

            if (configVerboseLogging.Value)
                Debug.Log($"{PluginName}: Initialized version {PluginVersion}.");
        }

        private void loadAssets()
        {
            try
            {
                // Log all embedded resources for debugging
                var execAssembly = Assembly.GetExecutingAssembly();
                var resourceNames = execAssembly.GetManifestResourceNames();
                if (configVerboseLogging.Value)
                    Debug.Log($"{PluginName}: Embedded resources: {string.Join(", ", resourceNames)}");

                assetBundle = GetAssetBundleFromResources("verdantsascent");
                if (assetBundle == null)
                {
                    Debug.LogError($"{PluginName}: Failed to load asset bundle!");
                    return;
                }
                var assetNames = assetBundle.GetAllAssetNames();
                if (configVerboseLogging.Value)
                    Debug.Log($"{PluginName}: Available assets in bundle: {string.Join(", ", assetNames)}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{PluginName}: Error loading assets: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private AssetBundle GetAssetBundleFromResources(string filename)
        {
            var execAssembly = Assembly.GetExecutingAssembly();
            var resourceName = execAssembly.GetManifestResourceNames().SingleOrDefault(str => str.EndsWith(filename, StringComparison.OrdinalIgnoreCase));
            if (resourceName == null)
            {
                Debug.LogError($"{PluginName}: No resource found ending with {filename}.");
                return null;
            }
            if (configVerboseLogging.Value)
                Debug.Log($"{PluginName}: Loading asset bundle from resource: {resourceName}");
            using (var stream = execAssembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    Debug.LogError($"{PluginName}: Failed to open stream for resource {resourceName}.");
                    return null;
                }
                return AssetBundle.LoadFromStream(stream);
            }
        }

        private void ValidateConfigs()
        {
            var recipeStrings = configTauntHammerRecipe.Value.Split(',').ToList();
            if (recipeStrings.Count % 3 != 0)
            {
                Debug.LogWarning($"{PluginName}: Invalid recipe config for TauntHammer_vad - resetting to default.");
                configTauntHammerRecipe.Value = configTauntHammerRecipe.DefaultValue.ToString();
            }
            for (int i = 1; i < recipeStrings.Count; i += 3)
            {
                if (!int.TryParse(recipeStrings[i], out _) || !int.TryParse(recipeStrings[i + 1], out _))
                {
                    Debug.LogWarning($"{PluginName}: Invalid amounts in recipe config for TauntHammer_vad - resetting to default.");
                    configTauntHammerRecipe.Value = configTauntHammerRecipe.DefaultValue.ToString();
                    break;
                }
            }
        }

        private IEnumerator InitializeStatusEffects()
        {
            if (SceneManager.GetActiveScene().name == "start")
            {
                if (configVerboseLogging.Value)
                    Debug.Log($"{PluginName}: Main menu detected, skipping initialization.");
                yield break;
            }

            int maxAttempts = 200; // 20 seconds
            int attempts = 0;
            float approximateTimeSpent = 0f;
            const float maxTime = 20f;
            while (!IsObjectDBValid())
            {
                if (configVerboseLogging.Value)
                    Debug.Log($"{PluginName}: Waiting for ObjectDB (attempt {attempts + 1}/{maxAttempts})...");
                attempts++;
                if (approximateTimeSpent >= maxTime)
                {
                    Debug.LogError($"{PluginName}: ObjectDB failed to initialize after {maxAttempts} attempts.");
                    yield break;
                }
                yield return new WaitForSeconds(0.1f);
                approximateTimeSpent += 0.1f;
            }

            ValidateConfigs();
            SetupStatusEffects();
            AddTauntHammer();
            isInitialized = true;
            if (configVerboseLogging.Value)
                Debug.Log($"{PluginName}: Status effects and TauntHammer initialized.");
        }

        public void SetupStatusEffects()
        {
            if (!IsObjectDBValid())
            {
                if (configVerboseLogging.Value)
                    Debug.LogWarning($"{PluginName}: Skipping SetupStatusEffects - ObjectDB not valid.");
                return;
            }
            StatusEffectManager.Initialize();
        }

        public void AddTauntHammer()
        {
            if (!IsObjectDBValid())
            {
                if (configVerboseLogging.Value)
                    Debug.LogWarning($"{PluginName}: Skipping AddTauntHammer - ObjectDB not valid.");
                return;
            }

            // Check if already added
            if (ObjectDB.instance.GetItemPrefab("TauntHammer_vad") != null)
            {
                if (configVerboseLogging.Value)
                    Debug.Log($"{PluginName}: TauntHammer_vad already exists in ObjectDB.");
                return;
            }

            GameObject tauntHammer = null;
            string usedPath = null;
            if (assetBundle != null)
            {
                // Try multiple path variations
                string[] possiblePaths = new[]
                {
                    "assets/custom/vaitems/items/taunthammer_vad.prefab",
                    "Assets/Custom/VAitems/items/TauntHammer_vad.prefab",
                    "assets/custom/VAItems/Items/taunthammer_vad.prefab",
                    "Assets/Custom/VAItems/Items/TauntHammer_vad.prefab"
                };
                foreach (var path in possiblePaths)
                {
                    if (configVerboseLogging.Value)
                        Debug.Log($"{PluginName}: Attempting to load prefab from {path}");
                    tauntHammer = assetBundle.LoadAsset<GameObject>(path);
                    if (tauntHammer != null)
                    {
                        usedPath = path;
                        break;
                    }
                }
            }

            if (tauntHammer == null)
            {
                Debug.LogWarning($"{PluginName}: Failed to load TauntHammer_vad prefab. Attempted paths: {(assetBundle != null ? string.Join(", ", assetBundle.GetAllAssetNames()) : "No bundle")}. Falling back to cloning vanilla Hammer.");
                // Fallback to cloning vanilla Hammer
                GameObject vanillaHammer = ObjectDB.instance.GetItemPrefab("Hammer");
                if (vanillaHammer == null)
                {
                    Debug.LogError($"{PluginName}: Vanilla Hammer prefab not found.");
                    return;
                }
                tauntHammer = Instantiate(vanillaHammer);
                tauntHammer.name = "TauntHammer_vad";
                usedPath = "Vanilla Hammer (cloned)";
            }
            if (configVerboseLogging.Value)
                Debug.Log($"{PluginName}: Successfully loaded TauntHammer_vad prefab from {usedPath}.");

            // Get ItemDrop component
            ItemDrop itemDrop = tauntHammer.GetComponent<ItemDrop>();
            if (itemDrop == null)
            {
                Debug.LogError($"{PluginName}: ItemDrop component missing on TauntHammer_vad.");
                return;
            }

            // Customize shared data
            ItemDrop.ItemData.SharedData shared = itemDrop.m_itemData.m_shared;
            shared.m_name = "Taunt Hammer"; // Plain text; assumes prefab has proper icon
            shared.m_description = "A hammer that taunts enemies on hit.";
            shared.m_maxQuality = 4; // Example

            // Assign Taunt effect to attack
            TauntEffect tauntEffect = StatusEffectManager.GetEffect("Taunted") as TauntEffect;
            if (tauntEffect == null)
            {
                Debug.LogError($"{PluginName}: Taunted effect not found for TauntHammer_vad.");
                return;
            }
            shared.m_attackStatusEffect = tauntEffect;
            shared.m_attackStatusEffectChance = 1f; // 100% chance
            if (configVerboseLogging.Value)
                Debug.Log($"{PluginName}: Assigned Taunt effect to TauntHammer_vad (effect: {tauntEffect.name}, duration: {tauntEffect.Duration}s).");

            // Add to ObjectDB
            ObjectDB.instance.m_items.Add(tauntHammer);
            if (configVerboseLogging.Value)
                Debug.Log($"{PluginName}: Added TauntHammer_vad to ObjectDB. Effects in ObjectDB: {ObjectDB.instance.m_StatusEffects.Count}, Taunted present: {ObjectDB.instance.m_StatusEffects.Any(se => se.name == "Taunted")}.");

            // Create and add recipe
            Recipe recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = "Recipe_TauntHammer_vad";
            recipe.m_item = itemDrop;
            recipe.m_amount = 1;
            recipe.m_enabled = true;

            // Set crafting station (Workbench)
            GameObject stationPrefab = ZNetScene.instance?.GetPrefab("piece_workbench");
            if (stationPrefab != null)
            {
                recipe.m_craftingStation = stationPrefab.GetComponent<CraftingStation>();
                recipe.m_minStationLevel = 1;
            }

            // Parse recipe requirements
            recipe.m_resources = ParseRequirements(configTauntHammerRecipe.Value, "TauntHammer_vad");
            ObjectDB.instance.m_recipes.Add(recipe);

            // Add to player's known recipes
            if (Player.m_localPlayer != null)
            {
                var knownRecipes = (HashSet<string>)knownRecipesField?.GetValue(Player.m_localPlayer);
                if (knownRecipes != null && !knownRecipes.Contains(recipe.name))
                {
                    knownRecipes.Add(recipe.name);
                    if (configVerboseLogging.Value)
                        Debug.Log($"{PluginName}: Added Recipe_TauntHammer_vad to player's known recipes.");
                }
            }

            if (configVerboseLogging.Value)
                Debug.Log($"{PluginName}: Added TauntHammer_vad with Taunt effect and recipe.");
        }

        private Piece.Requirement[] ParseRequirements(string configValue, string key)
        {
            if (itemRecipeCache.TryGetValue(key, out var cachedReqs))
                return cachedReqs;

            if (configVerboseLogging.Value)
                Debug.Log($"{PluginName}: Parsing recipe for {key} - Config: {configValue}, ObjectDB items: {ObjectDB.instance?.m_items.Count ?? 0}, ZNetScene: {(ZNetScene.instance != null ? "Valid" : "Null")}");

            var materialStrings = configValue.Split(',');
            var reqs = new List<Piece.Requirement>();
            if (!IsObjectDBValid() || ZNetScene.instance == null)
            {
                if (configVerboseLogging.Value)
                    Debug.LogWarning($"{PluginName}: ObjectDB or ZNetScene not valid, using empty requirements for {key}.");
                itemRecipeCache[key] = reqs.ToArray();
                return reqs.ToArray();
            }

            for (int i = 0; i < materialStrings.Length; i += 3)
            {
                if (i + 2 >= materialStrings.Length) break;
                string matName = materialStrings[i].Trim();
                if (!int.TryParse(materialStrings[i + 1].Trim(), out int baseAmount))
                    baseAmount = 1;
                if (!int.TryParse(materialStrings[i + 2].Trim(), out int upgradeAmount))
                    upgradeAmount = 0;
                var resItem = ObjectDB.instance.GetItemPrefab(matName)?.GetComponent<ItemDrop>();
                if (resItem == null)
                {
                    if (configVerboseLogging.Value)
                        Debug.LogWarning($"{PluginName}: Invalid material '{matName}' in recipe for {key} - skipping.");
                    continue;
                }
                reqs.Add(new Piece.Requirement
                {
                    m_resItem = resItem,
                    m_amount = baseAmount,
                    m_amountPerLevel = upgradeAmount,
                    m_recover = true
                });
            }

            var reqsArray = reqs.ToArray();
            itemRecipeCache[key] = reqsArray;
            if (configVerboseLogging.Value && reqsArray.Length == 0)
                Debug.LogWarning($"{PluginName}: No valid requirements parsed for {key} - recipe may fail.");
            return reqsArray;
        }

        private void RegisterConsoleCommands()
        {
            new Terminal.ConsoleCommand("va_status_reload", "Reload RPGStatusEffects configs", (args) =>
            {
                if (isShuttingDown) return;
                Config.Reload();
                itemRecipeCache.Clear();
                ValidateConfigs();
                if (IsObjectDBValid())
                {
                    SetupStatusEffects();
                    AddTauntHammer();
                    Debug.Log($"{PluginName}: Configs reloaded and status effects/item updated.");
                }
                else
                {
                    Debug.LogWarning($"{PluginName}: Cannot reload configs, ObjectDB not valid.");
                }
            });
        }

        private void OnDestroy()
        {
            isShuttingDown = true;
            harmony.UnpatchSelf();
            if (assetBundle != null)
            {
                assetBundle.Unload(true);
                if (configVerboseLogging.Value)
                    Debug.Log($"{PluginName}: Unloaded asset bundle.");
            }
            if (configVerboseLogging.Value)
                Debug.Log($"{PluginName}: Unloaded.");
        }

        public static bool IsObjectDBValid()
        {
            return ObjectDB.instance != null;
        }

        [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
        public static class ObjectDB_CopyOtherDB_Patch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                if (!IsObjectDBValid()) return;
                Instance.ValidateConfigs();
                Instance.SetupStatusEffects();
                Instance.AddTauntHammer();
                Instance.isInitialized = true;
            }
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        public static class ObjectDB_Awake_Patch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                if (!IsObjectDBValid()) return;
                Instance.ValidateConfigs();
                Instance.SetupStatusEffects();
                Instance.AddTauntHammer();
                Instance.isInitialized = true;
            }
        }

        [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
        public static class TauntAIUpdatePatch
        {
            private static readonly MethodInfo setAlertedMethod = typeof(MonsterAI).GetMethod("SetAlerted", BindingFlags.NonPublic | BindingFlags.Instance);

            public static void Postfix(MonsterAI __instance, float dt)
            {
                if (__instance == null) return;
                var character = __instance.GetComponent<Character>();
                if (character == null || character.IsPlayer()) return;
                var seman = character.GetSEMan();
                if (seman == null) return;
                var tauntHash = "Taunted".GetStableHashCode();
                var taunt = seman.GetStatusEffect(tauntHash) as TauntEffect;
                if (taunt != null && taunt.Taunter != null && taunt.m_ttl > 0f)
                {
                    var targetField = typeof(MonsterAI).GetField("m_targetCreature", BindingFlags.NonPublic | BindingFlags.Instance);
                    targetField?.SetValue(__instance, taunt.Taunter);

                    // Handle flee suppression and aggression
                    var fleeField = typeof(MonsterAI).GetField("m_fleeIfHurtWhenTargetCantBeReached", BindingFlags.NonPublic | BindingFlags.Instance);
                    var lowHealthFleeField = typeof(MonsterAI).GetField("m_fleeIfLowHealth", BindingFlags.NonPublic | BindingFlags.Instance);
                    var huntPlayerField = typeof(MonsterAI).GetField("m_enableHuntPlayer", BindingFlags.NonPublic | BindingFlags.Instance);

                    if (!originalHuntStates.ContainsKey(character))
                    {
                        bool originalFlee = fleeField != null ? (bool)fleeField.GetValue(__instance) : false;
                        float originalLowHealthFlee = lowHealthFleeField != null ? (float)lowHealthFleeField.GetValue(__instance) : 0f;
                        bool originalHuntPlayer = huntPlayerField != null ? (bool)huntPlayerField.GetValue(__instance) : false;
                        originalHuntStates[character] = (originalFlee, originalLowHealthFlee > 0f, originalHuntPlayer);
                        if (Instance.configVerboseLogging.Value)
                        {
                            Debug.Log($"{PluginName}: Stored original states for {character.name}: fleeIfHurt={originalFlee}, fleeIfLowHealth={originalLowHealthFlee > 0f}, enableHuntPlayer={originalHuntPlayer}.");
                            if (fleeField != null)
                                Debug.Log($"{PluginName}: Set fleeIfHurtWhenTargetCantBeReached=false for {character.name} during taunt.");
                            if (lowHealthFleeField != null)
                                Debug.Log($"{PluginName}: Set fleeIfLowHealth=0 for {character.name} during taunt.");
                            if (huntPlayerField != null)
                                Debug.Log($"{PluginName}: Set enableHuntPlayer=true for {character.name} during taunt.");
                            if (setAlertedMethod != null)
                                Debug.Log($"{PluginName}: Called SetAlerted(true) for {character.name} during taunt.");
                            Debug.Log($"{PluginName}: Taunt locked enemy {character.name} to player (remaining: {taunt.m_ttl:F1}s).");
                        }
                        lastTauntLogTimes[character] = taunt.m_ttl;
                    }

                    // Apply state changes every frame to ensure continuous suppression
                    if (fleeField != null)
                        fleeField.SetValue(__instance, false); // Disable fleeing when hurt
                    if (lowHealthFleeField != null)
                        lowHealthFleeField.SetValue(__instance, 0f); // Disable fleeing at low health
                    if (huntPlayerField != null)
                        huntPlayerField.SetValue(__instance, true); // Force aggressive pursuit
                    if (setAlertedMethod != null)
                        setAlertedMethod.Invoke(__instance, new object[] { true }); // Force alerted state
                }
                else if (lastTauntLogTimes.Remove(character))
                {
                    if (originalHuntStates.TryGetValue(character, out var originalStates))
                    {
                        var fleeField = typeof(MonsterAI).GetField("m_fleeIfHurtWhenTargetCantBeReached", BindingFlags.NonPublic | BindingFlags.Instance);
                        var lowHealthFleeField = typeof(MonsterAI).GetField("m_fleeIfLowHealth", BindingFlags.NonPublic | BindingFlags.Instance);
                        var huntPlayerField = typeof(MonsterAI).GetField("m_enableHuntPlayer", BindingFlags.NonPublic | BindingFlags.Instance);

                        if (fleeField != null)
                        {
                            fleeField.SetValue(__instance, originalStates.fleeIfHurt);
                            if (Instance.configVerboseLogging.Value)
                                Debug.Log($"{PluginName}: Restored fleeIfHurtWhenTargetCantBeReached={originalStates.fleeIfHurt} for {character.name}.");
                        }
                        if (lowHealthFleeField != null)
                        {
                            lowHealthFleeField.SetValue(__instance, originalStates.fleeIfLowHealth ? 0.2f : 0f); // Restore to a reasonable default if true
                            if (Instance.configVerboseLogging.Value)
                                Debug.Log($"{PluginName}: Restored fleeIfLowHealth={(originalStates.fleeIfLowHealth ? 0.2f : 0f)} for {character.name}.");
                        }
                        if (huntPlayerField != null)
                        {
                            huntPlayerField.SetValue(__instance, originalStates.enableHuntPlayer);
                            if (Instance.configVerboseLogging.Value)
                                Debug.Log($"{PluginName}: Restored enableHuntPlayer={originalStates.enableHuntPlayer} for {character.name}.");
                        }
                        originalHuntStates.Remove(character);
                    }
                    if (Instance.configVerboseLogging.Value)
                        Debug.Log($"{PluginName}: Taunt ended on {character.name}.");
                }
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        public static class SetTauntOnDamagePatch
        {
            public static void Prefix(Character __instance, ref HitData hit)
            {
                if (hit == null || hit.m_attacker.IsNone() || __instance.IsPlayer() || hit.m_statusEffectHash != "Taunted".GetStableHashCode()) return;
                var attackerChar = ZNetScene.instance.FindInstance(hit.m_attacker)?.GetComponent<Character>();
                if (attackerChar == null) return;
                var seman = __instance.GetSEMan();
                if (seman == null) return;
                var taunt = seman.GetStatusEffect("Taunted".GetStableHashCode()) as TauntEffect;
                if (taunt == null)
                {
                    taunt = seman.AddStatusEffect("Taunted".GetStableHashCode(), true) as TauntEffect;
                }
                if (taunt != null)
                {
                    taunt.Taunter = attackerChar;
                    if (Instance.configVerboseLogging.Value)
                        Debug.Log($"{PluginName}: Applied taunt from player to {__instance.name}.");
                    // Apply Taunting effect to the attacker if it's a player
                    if (attackerChar.IsPlayer())
                    {
                        var playerSeman = attackerChar.GetSEMan();
                        var taunting = StatusEffectManager.GetEffect("Taunting") as TauntingEffect;
                        if (taunting != null)
                        {
                            taunting.Duration = taunt.Duration; // Mirror duration
                            playerSeman.AddStatusEffect(taunting, true);
                            if (Instance.configVerboseLogging.Value)
                                Debug.Log($"{PluginName}: Applied Taunting effect to player {attackerChar.name} for {taunting.Duration}s.");
                        }
                    }
                }
            }
        }
    }
}