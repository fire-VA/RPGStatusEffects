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
        public const string PluginVersion = "1.0.1";
        public ConfigSync configSync;
        public SyncedConfigEntry<bool> configVerboseLogging;
        public SyncedConfigEntry<float> configPurityDuration;
        public SyncedConfigEntry<float> configTauntDuration;
        public SyncedConfigEntry<string> configTauntHammerRecipe;
        private bool isInitialized;
        private bool isShuttingDown;
        public static AssetBundle assetBundle;
        public static readonly List<GameObject> itemPrefabs = new();
        public static readonly FieldInfo knownRecipesField = typeof(Player).GetField("m_knownRecipes", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly Dictionary<Character, float> lastTauntLogTimes = new Dictionary<Character, float>();
        private static readonly Dictionary<Character, (bool fleeIfHurt, bool fleeIfLowHealth, bool enableHuntPlayer)> originalHuntStates = new Dictionary<Character, (bool, bool, bool)>();

        private void Awake()
        {
            Instance = this;
            configSync = new ConfigSync(PluginGUID) { DisplayName = PluginName, CurrentVersion = PluginVersion, MinimumRequiredVersion = PluginVersion };
            configSync.lockedConfigChanged += () =>
            {
                if (isShuttingDown || !isInitialized || !IsObjectDBValid()) return;
                ValidateConfigs();
                SetupStatusEffects();
                RecipeManager.SetupRecipe();
                if (configVerboseLogging.Value)
                    Debug.Log($"{PluginName}: Config sync triggered status effect and recipe update.");
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
                assetBundle = GetAssetBundleFromResources("verdantsascent");
                if (assetBundle == null)
                {
                    Debug.LogError($"{PluginName}: Failed to load asset bundle!");
                    return;
                }
                if (configVerboseLogging.Value)
                {
                    var assetNames = assetBundle.GetAllAssetNames();
                    Debug.Log($"{PluginName}: Available assets in bundle: {string.Join(", ", assetNames)}");
                }
                LoadItems("Assets/Custom/VAitems/");
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

        private void LoadItems(string mainPath)
        {
            string[] itemNames = { "TauntHammer_vad" };
            itemPrefabs.Clear();
            foreach (string name in itemNames)
            {
                string prefabPath = mainPath + "items/" + name + ".prefab";
                GameObject itemPrefab = assetBundle?.LoadAsset<GameObject>(prefabPath);
                if (itemPrefab == null)
                {
                    if (configVerboseLogging.Value)
                        Debug.LogWarning($"{PluginName}: Could not find prefab: {prefabPath}");
                    continue;
                }
                var itemDrop = itemPrefab.GetComponent<ItemDrop>();
                if (itemDrop == null)
                {
                    if (configVerboseLogging.Value)
                        Debug.LogError($"{PluginName}: {name} - ItemDrop component missing!");
                    continue;
                }
                itemPrefab.SetActive(true);
                itemPrefabs.Add(itemPrefab);
                if (configVerboseLogging.Value)
                    Debug.Log($"{PluginName}: Loaded prefab: {name}, m_dropPrefab: {(itemDrop.m_itemData.m_dropPrefab != null ? itemDrop.m_itemData.m_dropPrefab.name : "null")}");
            }
        }

        public static void AddItems(List<GameObject> items)
        {
            try
            {
                foreach (GameObject item in items)
                {
                    if (item == null)
                    {
                        if (Instance.configVerboseLogging.Value)
                            Debug.LogWarning($"{PluginName}: Null item in list, skipping.");
                        continue;
                    }
                    var itemDrop = item.GetComponent<ItemDrop>();
                    if (itemDrop != null)
                    {
                        if (ObjectDB.instance.GetItemPrefab(item.name) == null)
                        {
                            ObjectDB.instance.m_items.Add(item);
                            Dictionary<int, GameObject> m_itemsByHash = (Dictionary<int, GameObject>)typeof(ObjectDB).GetField("m_itemByHash", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ObjectDB.instance);
                            m_itemsByHash[item.name.GetHashCode()] = item;
                            if (Instance.configVerboseLogging.Value)
                                Debug.Log($"{PluginName}: Added {item.name} to ObjectDB.m_items");
                        }
                        else if (Instance.configVerboseLogging.Value)
                        {
                            Debug.LogWarning($"{PluginName}: {item.name} already exists in ObjectDB, skipping.");
                        }
                    }
                    else if (Instance.configVerboseLogging.Value)
                    {
                        Debug.LogError($"{PluginName}: {item.name} - ItemDrop not found on prefab");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{PluginName}: Error adding items to ObjectDB: {ex.Message}");
            }
        }

        public static void AddPrefabsToZNetScene(ZNetScene zNetScene)
        {
            try
            {
                foreach (GameObject gm in itemPrefabs)
                {
                    if (gm == null)
                    {
                        if (Instance.configVerboseLogging.Value)
                            Debug.LogWarning($"{PluginName}: Null prefab in itemPrefabs list, skipping.");
                        continue;
                    }
                    GameObject found = zNetScene.m_prefabs.Find((x) => x != null && x.name == gm.name);
                    if (found == null)
                    {
                        zNetScene.m_prefabs.Add(gm);
                        if (Instance.configVerboseLogging.Value)
                            Debug.Log($"{PluginName}: Added {gm.name} to ZNetScene.m_prefabs");
                    }
                    else if (Instance.configVerboseLogging.Value)
                    {
                        Debug.LogWarning($"{PluginName}: Object exists in ZNetScene, skipping: {gm.name}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{PluginName}: Error adding prefabs to ZNetScene: {ex.Message}");
            }
            zNetScene.m_prefabs.RemoveAll((x) => x == null);
        }

        private void ValidateConfigs()
        {
            var recipeStrings = configTauntHammerRecipe.Value.Split(',').ToList();
            if (recipeStrings.Count % 3 != 0)
            {
                if (configVerboseLogging.Value)
                    Debug.LogWarning($"{PluginName}: Invalid recipe config for TauntHammer_vad - resetting to default.");
                configTauntHammerRecipe.Value = configTauntHammerRecipe.DefaultValue.ToString();
            }
            for (int i = 1; i < recipeStrings.Count; i += 3)
            {
                if (!int.TryParse(recipeStrings[i], out _) || !int.TryParse(recipeStrings[i + 1], out _))
                {
                    if (configVerboseLogging.Value)
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
            int maxAttempts = 100; // 10 seconds
            int attempts = 0;
            float approximateTimeSpent = 0f;
            const float maxTime = 10f;
            while (!IsObjectDBValid() || ZNetScene.instance == null || ZNetScene.instance.m_prefabs == null || ZNetScene.instance.m_prefabs.Count == 0)
            {
                if (configVerboseLogging.Value)
                    Debug.Log($"{PluginName}: Waiting for ObjectDB and ZNetScene (attempt {attempts + 1}/{maxAttempts})...");
                attempts++;
                if (approximateTimeSpent >= maxTime)
                {
                    Debug.LogError($"{PluginName}: ObjectDB and ZNetScene failed to initialize after {maxAttempts} attempts.");
                    yield break;
                }
                yield return new WaitForSeconds(0.1f);
                approximateTimeSpent += 0.1f;
            }
            ValidateConfigs();
            SetupStatusEffects();
            RecipeManager.SetupRecipe();
            isInitialized = true;
            if (configVerboseLogging.Value)
                Debug.Log($"{PluginName}: Status effects and recipe initialized.");
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

        private void RegisterConsoleCommands()
        {
            new Terminal.ConsoleCommand("va_status_reload", "Reload RPGStatusEffects configs", (args) =>
            {
                if (isShuttingDown) return;
                Config.Reload();
                RecipeManager.ClearCache();
                ValidateConfigs();
                if (IsObjectDBValid())
                {
                    SetupStatusEffects();
                    RecipeManager.SetupRecipe();
                    if (configVerboseLogging.Value)
                        Debug.Log($"{PluginName}: Configs reloaded and status effects/recipe updated.");
                }
                else
                {
                    if (configVerboseLogging.Value)
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

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        public static class ObjectDB_Awake_Path
        {
            public static void Postfix()
            {
                if (!IsObjectDBValid()) return;
                AddItems(itemPrefabs);
                Instance.SetupStatusEffects();
                RecipeManager.SetupRecipe();
            }
        }

        [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
        public static class Object_CopyOtherDB_Path
        {
            public static void Postfix()
            {
                if (!IsObjectDBValid())
                {
                    if (Instance.configVerboseLogging.Value)
                        Debug.LogWarning($"{PluginName}: ObjectDB not valid in CopyOtherDB, skipping item addition.");
                    return;
                }
                AddItems(itemPrefabs);
                Instance.SetupStatusEffects();
            }
        }

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        public static class ZNetScene_Awake_Path
        {
            public static void Prefix(ZNetScene __instance)
            {
                if (__instance == null)
                {
                    if (Instance.configVerboseLogging.Value)
                        Debug.LogWarning($"{PluginName}: No ZNetScene found");
                    return;
                }
                AddPrefabsToZNetScene(__instance);
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
                    if (fleeField != null)
                        fleeField.SetValue(__instance, false);
                    if (lowHealthFleeField != null)
                        lowHealthFleeField.SetValue(__instance, 0f);
                    if (huntPlayerField != null)
                        huntPlayerField.SetValue(__instance, true);
                    if (setAlertedMethod != null)
                        setAlertedMethod.Invoke(__instance, new object[] { true });
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
                            lowHealthFleeField.SetValue(__instance, originalStates.fleeIfLowHealth ? 0.2f : 0f);
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
                    if (attackerChar.IsPlayer())
                    {
                        var playerSeman = attackerChar.GetSEMan();
                        var taunting = StatusEffectManager.GetEffect("Taunting") as TauntingEffect;
                        if (taunting != null)
                        {
                            taunting.Duration = taunt.Duration;
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