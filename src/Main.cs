using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using LitJson;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Ostranauts.Core.Tutorials;
using Ostranauts.Objectives;
using Ostranauts.ShipGUIs.MFD;

using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.NavStation;
using Ostranauts.Ships;

namespace OstranautsRuKaya
{
    [BepInPlugin("ru.kaya.ostranautsrukaya", "OstranautsRuKaya", "1.0.0")]
    public class RuTranslation : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        internal static Dictionary<string, string[]> VerbConjugations = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        internal static Dictionary<string, TutorialTranslation> TutorialTranslations = new Dictionary<string, TutorialTranslation>(StringComparer.OrdinalIgnoreCase);

        internal sealed class TutorialTranslation
        {
            public string Name;
            public string Desc;
            public string Complete;
        }

        internal static string TryGetInfinitive(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (s.EndsWith("ется") || s.EndsWith("ится"))
                return s.Substring(0, s.Length - 4) + "иться";
            if (s.EndsWith("ает") || s.EndsWith("яет") || s.EndsWith("аёет"))
                return s.Substring(0, s.Length - 3) + "ать";
            if (s.EndsWith("ует") || s.EndsWith("юет"))
                return s.Substring(0, s.Length - 3) + "овать";
            if (s.EndsWith("ёт") || s.EndsWith("ет"))
                return s.Substring(0, s.Length - 2) + "ти";
            if (s.EndsWith("ит"))
                return s.Substring(0, s.Length - 2) + "ить";
            return null;
        }

        private static void LoadConjugations()
        {
            string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", "verb_conjugations.json");
            try
            {
                if (!File.Exists(path))
                {
                    Log?.LogError($"[Kaya] verb_conjugations.json not found at {path}");
                    return;
                }
                string json = File.ReadAllText(path, Encoding.UTF8);
                var arr = JsonMapper.ToObject(json);
                int count = 0;
                foreach (JsonData item in (JsonData)arr["verbs"])
                {
                    string inf = (string)item["infinitive"];
                    var formsToken = (JsonData)item["forms"];
                    string[] forms = new string[6];
                    int i = 0;
                    foreach (JsonData f in (JsonData)formsToken)
                    {
                        if (i >= 6) break;
                        forms[i++] = (string)f;
                    }
                    while (i < 6) forms[i++] = inf;

                    VerbConjugations[inf] = forms;
                    string sg3 = forms[2];
                    if (!string.IsNullOrEmpty(sg3) && !VerbConjugations.ContainsKey(sg3))
                        VerbConjugations[sg3] = forms;
                    count++;
                }
                Log?.LogInfo($"[Kaya] Loaded {count} verb conjugations");
            }
            catch (Exception ex)
            {
                Log?.LogError($"[Kaya] Failed to load verb_conjugations.json: {ex}");
            }
        }

        private static void LoadTutorialTranslations()
        {
            string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", "tutorial_translations.json");
            try
            {
                if (!File.Exists(path))
                {
                    Log?.LogWarning($"[Kaya] tutorial_translations.json not found at {path}");
                    return;
                }
                var root = JsonMapper.ToObject(File.ReadAllText(path, Encoding.UTF8));
                int count = 0;
                foreach (JsonData item in (JsonData)root["tutorials"])
                {
                    string type = (string)item["type"];
                    TutorialTranslations[type] = new TutorialTranslation
                    {
                        Name = item.Keys.Contains("name") ? (string)item["name"] : null,
                        Desc = item.Keys.Contains("desc") ? (string)item["desc"] : null,
                        Complete = item.Keys.Contains("complete") ? (string)item["complete"] : null
                    };
                    count++;
                }
                Log?.LogInfo($"[Kaya] Loaded {count} tutorial translations");
            }
            catch (Exception ex)
            {
                Log?.LogError($"[Kaya] Failed to load tutorial_translations.json: {ex}");
            }
        }

        private static void LoadTranslations()
        {
            string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", "translations.json");
            TranslationData.LoadFromJson(path);
        }

        internal static string FormatTutorialText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string[] glyphKeys = {
                "Pause", "Show Hotkeys & Interactables", "Click", "Player Inventory",
                "RightClick", "Quick Move Item", "Toggle PDA Power Vizor", "Turn CW",
                "Turn CCW", "Attitude", "Thrust Down", "Thrust Up", "Thrust Right",
                "Thrust Left", "Toggle station keeping"
            };
            foreach (string key in glyphKeys)
            {
                string marker = "{glyph:" + key + "}";
                if (text.Contains(marker))
                {
                    string glyph = GetInputGlyph(key);
                    if (!string.IsNullOrEmpty(glyph)) text = text.Replace(marker, glyph);
                }
            }
            return text;
        }

        private static string GetInputGlyph(string key)
        {
            try
            {
                Type type = AccessTools.TypeByName("InputManager") ?? AccessTools.TypeByName("Ostranauts.InputControl.InputManager");
                MethodInfo method = type?.GetMethod(
                    "GetGlyphString",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new Type[] { typeof(string), typeof(string) },
                    null);
                return method?.Invoke(null, new object[] { key, null }) as string;
            }
            catch { return null; }
        }

        internal static bool TryGetTutorialTranslation(TutorialBeat beat, out TutorialTranslation translation)
        {
            translation = null;
            return beat != null && TutorialTranslations.TryGetValue(beat.GetType().Name, out translation);
        }

        internal static void ApplyTutorialTranslation(TutorialBeat beat, Objective objective)
        {
            if (!TryGetTutorialTranslation(beat, out var tr) || objective == null) return;
            if (tr.Name != null) objective.strDisplayName = FormatTutorialText(tr.Name);
            if (tr.Desc != null) objective.strDisplayDesc = FormatTutorialText(tr.Desc);
            if (tr.Complete != null) objective.strDisplayDescComplete = FormatTutorialText(tr.Complete);
        }

        private int _scanFrameCounter = 0;

        private void Awake()
        {
            try
            {
                Log = Logger;
                Log.LogInfo("[Kaya] Plugin starting...");

                LoadTranslations();
                LoadConjugations();
                LoadTutorialTranslations();
                ReplaceGrammarDictionaries();
                Log.LogInfo("[Kaya] Grammar replaced");

                var harmony = new Harmony("ru.kaya.ostranautsrukaya");
                harmony.PatchAll();
                Log.LogInfo("[Kaya] Harmony patches applied");

                Log.LogInfo("[Kaya] Ru Translation loaded");
            }
            catch (Exception ex)
            {
                Log?.LogError($"[Kaya] Awake failed: {ex}");
            }
        }

        private bool _dumpedOnce = false;
        private int _dumpCounter = 0;

        // ─── Periodic scanner: runs EVERY frame from the plugin's own MonoBehaviour ───
        // This is the ONLY reliable way to catch prefab-deserialized text (PLA, SIGNAL, ON, OFF etc.)
        // because Unity deserialization bypasses C# set_text, and GUIOrbitDraw.Update only
        // fires when the orbit screen is open.
        private void Update()
        {
            try
            {
                _scanFrameCounter++;
                if (_scanFrameCounter < 300) return; // every ~5 seconds at 60fps
                _scanFrameCounter = 0;

                // ── Method 1: FindObjectsOfType for active components ──
                var texts = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>();
                foreach (var t in texts)
                {
                    if (t != null && !string.IsNullOrEmpty(t.text))
                    {
                        string translated = HUDTranslation.TranslateString(t.text);
                        if (translated != t.text)
                            t.text = translated;
                    }
                }

                var tmpTexts = UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>();
                foreach (var t in tmpTexts)
                {
                    if (t != null && !string.IsNullOrEmpty(t.text))
                    {
                        string translated = HUDTranslation.TranslateString(t.text);
                        if (translated != t.text)
                            t.text = translated;
                    }
                }

                // ── Method 2: Traverse ALL root objects including INACTIVE children ──
                // NavMod panels may be inactive until player interacts with NavStation.
                // GetComponentsInChildren(true) includes inactive children.
                var rootObjects = UnityEngine.SceneManagement.SceneManager
                    .GetActiveScene().GetRootGameObjects();
                foreach (var root in rootObjects)
                {
                    if (root == null) continue;
                    // Get ALL TMP_Text including inactive
                    var inactiveTmp = root.GetComponentsInChildren<TMPro.TMP_Text>(true);
                    foreach (var t in inactiveTmp)
                    {
                        if (t != null && !string.IsNullOrEmpty(t.text))
                        {
                            string translated = HUDTranslation.TranslateString(t.text);
                            if (translated != t.text)
                                t.text = translated;
                        }
                    }
                    // Get ALL UI.Text including inactive
                    var inactiveText = root.GetComponentsInChildren<UnityEngine.UI.Text>(true);
                    foreach (var t in inactiveText)
                    {
                        if (t != null && !string.IsNullOrEmpty(t.text))
                        {
                            string translated = HUDTranslation.TranslateString(t.text);
                            if (translated != t.text)
                                t.text = translated;
                        }
                    }
                }
            }
            catch { }
        }

        private static void RegisterVerbsInDictVerbs()
        {
            try
            {
                var dictVerbsField = typeof(GrammarUtils).GetField("dictVerbs",
                    BindingFlags.Static | BindingFlags.Public);
                if (dictVerbsField == null) return;
                var dictVerbs = dictVerbsField.GetValue(null) as IDictionary;
                if (dictVerbs == null) return;

                int added = 0;
                foreach (var kvp in VerbConjugations)
                {
                    string key = kvp.Key;
                    string[] forms = kvp.Value;
                    if (forms == null || forms.Length < 6) continue;
                    string[] verbForms = new string[2] { forms[2], forms[1] };
                    if (!dictVerbs.Contains(key))
                    {
                        dictVerbs[key] = verbForms;
                        added++;
                    }
                }
                Log?.LogInfo($"[Kaya] Added {added} Russian verbs to dictVerbs");
            }
            catch (Exception ex)
            {
                Log?.LogError($"[Kaya] RegisterVerbsInDictVerbs failed: {ex}");
            }
        }

        private void ReplaceGrammarDictionaries()
        {
            var bf = BindingFlags.Static | BindingFlags.Public;
            var pos = typeof(GrammarUtils).GetField("partsOfSpeech", bf)?.GetValue(null)
                as Dictionary<GrammarUtils.GrammarLUTIndex, string[]>;
            var posc = typeof(GrammarUtils).GetField("partsOfSpeechSentenceCase", bf)?.GetValue(null)
                as Dictionary<GrammarUtils.GrammarLUTIndex, string[]>;

            if (pos == null || posc == null)
            {
                Log?.LogError("[Kaya] Could not locate partsOfSpeech dictionaries");
                return;
            }

            string[] subj = { "я", "ты", "он", "она", "они", "оно" };
            string[] poss = { "мой", "твой", "его", "её", "их", "его" };
            string[] obj = { "меня", "тебя", "его", "её", "их", "его" };
            string[] refl = { "себя", "себя", "себя", "себя", "себя", "себя" };
            string[] cIs = { "я", "ты", "он", "она", "они", "оно" };
            string[] cHas = { "у меня", "у тебя", "у него", "у неё", "у них", "у него" };
            string[] cWill = { "я", "ты", "он", "она", "они", "оно" };
            string[] SubjS = { "Я", "Ты", "Он", "Она", "Они", "Оно" };
            string[] PossS = { "Мой", "Твой", "Его", "Её", "Их", "Его" };
            string[] ObjS = { "Меня", "Тебя", "Его", "Её", "Их", "Его" };
            string[] RefS = { "Себя", "Себя", "Себя", "Себя", "Себя", "Себя" };
            string[] CIsS = { "Я", "Ты", "Он", "Она", "Они", "Оно" };
            string[] CWillS = { "Я", "Ты", "Он", "Она", "Они", "Оно" };

            pos.Clear();
            posc.Clear();
            pos[GrammarUtils.GrammarLUTIndex.Subjective] = subj;
            pos[GrammarUtils.GrammarLUTIndex.Possessive] = poss;
            pos[GrammarUtils.GrammarLUTIndex.Objective] = obj;
            pos[GrammarUtils.GrammarLUTIndex.Reflexive] = refl;
            pos[GrammarUtils.GrammarLUTIndex.ContractIs] = cIs;
            pos[GrammarUtils.GrammarLUTIndex.ContractHas] = cHas;
            pos[GrammarUtils.GrammarLUTIndex.ContractWill] = cWill;
            posc[GrammarUtils.GrammarLUTIndex.Subjective] = SubjS;
            posc[GrammarUtils.GrammarLUTIndex.Possessive] = PossS;
            posc[GrammarUtils.GrammarLUTIndex.Objective] = ObjS;
            posc[GrammarUtils.GrammarLUTIndex.Reflexive] = RefS;
            posc[GrammarUtils.GrammarLUTIndex.ContractIs] = CIsS;
            posc[GrammarUtils.GrammarLUTIndex.ContractWill] = CWillS;

            Log?.LogInfo($"[Kaya] Grammar replaced: {pos.Count}/{posc.Count} entries");

            RegisterVerbsInDictVerbs();
        }
    }

    // ─── Verb Conjugation ───

    [HarmonyPatch(typeof(DataHandler), "PrepareToken")]
    public static class Patch_GrammarUtils_PrepareToken
    {
        static void Prefix(ref TokenData t, string[] args)
        {
            try
            {
                if (args == null || args.Length != 1 || string.IsNullOrEmpty(args[0])) return;
                string key = args[0];
                if (RuTranslation.VerbConjugations == null ||
                    !RuTranslation.VerbConjugations.ContainsKey(key)) return;

                t.output = GrammarUtils.Verb;
                t.verbForms = new string[2] { key, key };
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(GrammarUtils), "Verb")]
    public static class Patch_GrammarUtils_Verb
    {
        static bool Prefix(ref TokenData tokenData)
        {
            try
            {
                var entityMapField = typeof(GrammarUtils).GetField("entityMap",
                    BindingFlags.Static | BindingFlags.Public);
                var outputField = typeof(GrammarUtils).GetField("interactionOutput",
                    BindingFlags.Static | BindingFlags.Public);

                var entityMap = entityMapField?.GetValue(null) as IDictionary;
                var sb = outputField?.GetValue(null) as StringBuilder;
                if (entityMap == null || sb == null) return true;

                if (!entityMap.Contains(tokenData.alias)) return true;
                var valueObj = entityMap[tokenData.alias];
                if (valueObj == null) return true;

                var inflectionField = valueObj.GetType().GetField("InflectionIndex",
                    BindingFlags.Instance | BindingFlags.Public);
                if (inflectionField == null) return true;
                int inflectionIdx = (int)inflectionField.GetValue(valueObj);

                if (tokenData.verbForms == null || tokenData.verbForms.Length == 0) return true;
                if (RuTranslation.VerbConjugations == null || RuTranslation.VerbConjugations.Count == 0) return true;

                string[] conjugations = null;
                if (tokenData.verbForms.Length > 0)
                    RuTranslation.VerbConjugations.TryGetValue(tokenData.verbForms[0], out conjugations);
                if (conjugations == null && tokenData.verbForms.Length > 1)
                    RuTranslation.VerbConjugations.TryGetValue(tokenData.verbForms[1], out conjugations);
                if (conjugations == null)
                {
                    foreach (var vf in tokenData.verbForms)
                    {
                        string inf = RuTranslation.TryGetInfinitive(vf);
                        if (inf != null && RuTranslation.VerbConjugations.TryGetValue(inf, out conjugations))
                            break;
                        conjugations = null;
                    }
                }
                if (conjugations == null) return true;

                int formIdx;
                if (inflectionIdx == 0) formIdx = 0;
                else if (inflectionIdx == 1) formIdx = 1;
                else if (inflectionIdx == 2 || inflectionIdx == 3) formIdx = 2;
                else if (inflectionIdx == 4) formIdx = 5;
                else if (inflectionIdx == 5) formIdx = 2;
                else formIdx = 2;

                if (formIdx >= conjugations.Length) formIdx = 2;
                string ruForm = conjugations[formIdx];

                if (GrammarUtils.Capitalise())
                {
                    if (ruForm.Length > 0)
                        ruForm = char.ToUpper(ruForm[0]) + ruForm.Substring(1);
                }
                sb.Append(ruForm);
                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    // ─── Article Removal ───

    [HarmonyPatch(typeof(GrammarUtils), "AttemptProperName")]
    public static class Patch_GrammarUtils_AttemptProperName
    {
        static void Postfix(TokenData tokenData)
        {
            try
            {
                if (string.IsNullOrEmpty(tokenData.alias) ||
                    !GrammarUtils.entityMap.TryGetValue(tokenData.alias, out var entity) ||
                    entity == null ||
                    entity.InflectionIndex != GrammarUtils.PronounInflection.ThirdNeuterNonHuman)
                    return;

                var sb = GrammarUtils.interactionOutput;
                int caret = GrammarUtils.caret;
                if (sb == null || caret < 3 || caret >= sb.Length) return;
                string article = sb.ToString(caret - 3, 4);
                if (article == "the " || article == "The ")
                {
                    sb.Remove(caret - 3, 4);
                    GrammarUtils.caret = caret - 4;
                }
            }
            catch { }
        }
    }

    // ─── Tutorial Translations ───

    [HarmonyPatch(typeof(Objective), "MakeTutorialObjective")]
    public static class Patch_Objective_MakeTutorialObjective
    {
        static void Postfix(TutorialBeat tutorialBeat, Objective __result)
        {
            try { RuTranslation.ApplyTutorialTranslation(tutorialBeat, __result); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(ObjectiveTracker), "AddObjective")]
    public static class Patch_ObjectiveTracker_AddObjective
    {
        static void Prefix(Objective objective)
        {
            try
            {
                if (objective != null && objective.TutorialBeat != null)
                    RuTranslation.ApplyTutorialTranslation(objective.TutorialBeat, objective);
            }
            catch { }
        }
    }

    // ─── NavStation Restore Fix ───

    [HarmonyPatch(typeof(RestoreNavStation), "OnQuickActionButton")]
    public static class Patch_RestoreNavStation_QuickActionButton
    {
        static void Postfix(RestoreNavStation __instance, GUIQuickActionButton qab)
        {
            try
            {
                if (__instance == null || __instance.Finished || qab == null || qab.IA == null) return;
                var ia = qab.IA;
                if (ia.objThem != null && CrewSimTut.playerShipNavStationRef != null &&
                    ia.objThem.strID == CrewSimTut.playerShipNavStationRef.strID &&
                    ia.strDuty == "Restore")
                {
                    __instance.Finished = true;
                }
            }
            catch { }
        }
    }

    // ─── Objective Panel Refresh ───

    [HarmonyPatch(typeof(ObjectivePanel), "RefreshText")]
    public static class Patch_ObjectivePanel_RefreshText
    {
        static void Postfix(ObjectivePanel __instance)
        {
            try
            {
                var objectiveField = AccessTools.Field(typeof(ObjectivePanel), "_objective");
                var objective = objectiveField?.GetValue(__instance) as Objective;
                if (objective == null || objective.TutorialBeat == null ||
                    !RuTranslation.TryGetTutorialTranslation(objective.TutorialBeat, out var tr)) return;

                var titleField = AccessTools.Field(typeof(ObjectivePanel), "_txtTitle");
                var descField = AccessTools.Field(typeof(ObjectivePanel), "_txtDescription");
                if (titleField?.GetValue(__instance) is object title && tr.Name != null)
                    title.GetType().GetProperty("text")?.SetValue(title, RuTranslation.FormatTutorialText(tr.Name), null);
                if (descField?.GetValue(__instance) is object desc && tr.Desc != null)
                    desc.GetType().GetProperty("text")?.SetValue(desc, RuTranslation.FormatTutorialText(tr.Desc), null);
            }
            catch { }
        }
    }

    // ─── LogMessage Patch (from original SerJo2 plugin) ───

    [HarmonyPatch(typeof(CondOwner), nameof(CondOwner.LogMessage))]
    public static class Patch_CondOwner_LogMessage
    {
        static void Prefix(ref string strMsg)
        {
            if (!string.IsNullOrEmpty(strMsg))
            {
                foreach (var kvp in TranslationData.ReplacementsGrammar)
                    strMsg = strMsg.Replace(kvp.Key, kvp.Value);
            }
        }
    }

    // ─── MFD HUD Labels Translation ───

    public static class MFDTranslate
    {
        internal static void ReplaceInList(List<string> list, string oldText, string newText)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Contains(oldText))
                    list[i] = list[i].Replace(oldText, newText);
            }
        }
    }

    // MFDMainMenu: translates title and rebuilds left/right lists
    [HarmonyPatch(typeof(MFDMainMenu), "get_Title")]
    public static class Patch_MFDMainMenu_Title
    {
        static void Postfix(ref string __result)
        {
            __result = HUDTranslation.TranslateString(__result);
        }
    }

    [HarmonyPatch(typeof(MFDMainMenu), "RebuildMenu")]
    public static class Patch_MFDMainMenu_RebuildMenu
    {
        static void Postfix(MFDMainMenu __instance)
        {
            try
            {
                var leftField = AccessTools.Field(typeof(MFDMainMenu), "_left");
                var rightField = AccessTools.Field(typeof(MFDMainMenu), "_right");
                var left = leftField?.GetValue(__instance) as List<string>;
                var right = rightField?.GetValue(__instance) as List<string>;

                if (left != null)
                    HUDTranslation.ApplyMfdReplacements(left);
                if (right != null)
                    HUDTranslation.ApplyMfdReplacements(right);
            }
            catch { }
        }
    }

    // MFDDockInfo: translates dock info labels
    [HarmonyPatch(typeof(MFDDockInfo), "BuildMenu")]
    public static class Patch_MFDDockInfo_BuildMenu
    {
        static void Postfix(MFDDockInfo __instance)
        {
            try
            {
                var leftField = AccessTools.Field(typeof(MFDPage), "<Left>k__BackingField");
                var left = leftField?.GetValue(__instance) as List<string>;
                if (left != null)
                    HUDTranslation.ApplyMfdReplacements(left);
            }
            catch { }
        }
    }

    // MFDInbox: translates title
    [HarmonyPatch(typeof(MFDInbox), "get_Title")]
    public static class Patch_MFDInbox_Title
    {
        static void Postfix(ref string __result)
        {
            __result = HUDTranslation.TranslateString(__result);
        }
    }

    // MFDMessageLog: translates right panel labels
    [HarmonyPatch(typeof(MFDMessageLog), "get_Right")]
    public static class Patch_MFDMessageLog_Right
    {
        static void Postfix(ref List<string> __result)
        {
            try
            {
                if (__result != null)
                    HUDTranslation.ApplyMfdReplacements(__result);
            }
            catch { }
        }
    }

    // MFDComms: removed (get_Title inherited from MFDPage, caused PatchAll crash)

    // ─── Tutorial/Objective HUD patches ───

    [HarmonyPatch(typeof(CollectEquipment), "get_ObjectiveName")]
    public static class Patch_CollectEquipment_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "CollectEquipment.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(CrowbarHallway3), "get_ObjectiveDescComplete")]
    public static class Patch_CrowbarHallway3_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            string key = "CrowbarHallway3.ObjectiveDescComplete";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(CrowbarHallway4), "get_ObjectiveName")]
    public static class Patch_CrowbarHallway4_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "CrowbarHallway4.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(CrowbarHallway5), "get_ObjectiveDescComplete")]
    public static class Patch_CrowbarHallway5_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            string key = "CrowbarHallway5.ObjectiveDescComplete";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    
    // REMOVED: DeployLeo_ObjectiveName (method does not exist)


    [HarmonyPatch(typeof(DismissNote), "get_ObjectiveName")]
    public static class Patch_DismissNote_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "DismissNote.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(DismissNote), "get_ObjectiveDescComplete")]
    public static class Patch_DismissNote_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            string key = "DismissNote.ObjectiveDescComplete";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(DockWithDerelict), "get_ObjectiveName")]
    public static class Patch_DockWithDerelict_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "DockWithDerelict.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(DockWithDerelict), "get_ObjectiveDescComplete")]
    public static class Patch_DockWithDerelict_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            string key = "DockWithDerelict.ObjectiveDescComplete";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    
    // REMOVED: Dodge_ObjectiveName (method does not exist)


    [HarmonyPatch(typeof(ExpandMTT), "get_ObjectiveName")]
    public static class Patch_ExpandMTT_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "ExpandMTT.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(ExploreTutorialDerelict), "get_ObjectiveName")]
    public static class Patch_ExploreTutorialDerelict_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "ExploreTutorialDerelict.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    
    // REMOVED: FlyTo_ObjectiveName (method does not exist)


    
    // REMOVED: FlyToPath_ObjectiveName (method does not exist)


    [HarmonyPatch(typeof(GainClearance), "get_ObjectiveName")]
    public static class Patch_GainClearance_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "GainClearance.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(GainClearance), "get_ObjectiveDescComplete")]
    public static class Patch_GainClearance_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            string key = "GainClearance.ObjectiveDescComplete";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(HallwayConduit7), "get_ObjectiveName")]
    public static class Patch_HallwayConduit7_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "HallwayConduit7.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(HallwayConduit9), "get_ObjectiveName")]
    public static class Patch_HallwayConduit9_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "HallwayConduit9.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(HelmetAtmosphereUnsafe), "get_ObjectiveName")]
    public static class Patch_HelmetAtmosphereUnsafe_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "HelmetAtmosphereUnsafe.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(MatchSpeed), "get_ObjectiveDescComplete")]
    public static class Patch_MatchSpeed_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            string key = "MatchSpeed.ObjectiveDescComplete";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(NavUseShow), "get_ObjectiveName")]
    public static class Patch_NavUseShow_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "NavUseShow.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(NavWalk), "get_ObjectiveName")]
    public static class Patch_NavWalk_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "NavWalk.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(PayDockingFee), "get_ObjectiveName")]
    public static class Patch_PayDockingFee_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "PayDockingFee.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(PayDockingFee), "get_ObjectiveDescComplete")]
    public static class Patch_PayDockingFee_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            string key = "PayDockingFee.ObjectiveDescComplete";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(PrepareToExploreDerelict), "get_ObjectiveName")]
    public static class Patch_PrepareToExploreDerelict_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "PrepareToExploreDerelict.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(ReachBridgeTest), "get_ObjectiveName")]
    public static class Patch_ReachBridgeTest_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "ReachBridgeTest.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(RefuelAtKiosk), "get_ObjectiveDescComplete")]
    public static class Patch_RefuelAtKiosk_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            string key = "RefuelAtKiosk.ObjectiveDescComplete";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(RequestClearance), "get_ObjectiveName")]
    public static class Patch_RequestClearance_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "RequestClearance.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(RestoreNavStation), "get_ObjectiveName")]
    public static class Patch_RestoreNavStation_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "RestoreNavStation.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(RosterPermission), "get_ObjectiveName")]
    public static class Patch_RosterPermission_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "RosterPermission.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(SelectCompartment), "get_ObjectiveName")]
    public static class Patch_SelectCompartment_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "SelectCompartment.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(SellSalvageAtKiosk), "get_ObjectiveDescComplete")]
    public static class Patch_SellSalvageAtKiosk_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            string key = "SellSalvageAtKiosk.ObjectiveDescComplete";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(SwitchNav), "get_ObjectiveName")]
    public static class Patch_SwitchNav_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "SwitchNav.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(SwitchNav), "get_ObjectiveDescComplete")]
    public static class Patch_SwitchNav_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            string key = "SwitchNav.ObjectiveDescComplete";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(TargetDerelict), "get_ObjectiveName")]
    public static class Patch_TargetDerelict_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "TargetDerelict.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(TargetOKLG), "get_ObjectiveName")]
    public static class Patch_TargetOKLG_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "TargetOKLG.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(ToggleOffMatchSpeed), "get_ObjectiveDescComplete")]
    public static class Patch_ToggleOffMatchSpeed_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            string key = "ToggleOffMatchSpeed.ObjectiveDescComplete";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(TravelToDerelict), "get_ObjectiveName")]
    public static class Patch_TravelToDerelict_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "TravelToDerelict.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(TutorialEnd), "get_ObjectiveName")]
    public static class Patch_TutorialEnd_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "TutorialEnd.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(TutorialStub), "get_ObjectiveName")]
    public static class Patch_TutorialStub_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "TutorialStub.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(UnpaidDockingFees), "get_ObjectiveName")]
    public static class Patch_UnpaidDockingFees_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "UnpaidDockingFees.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(UnpaidDockingFees), "get_ObjectiveDescComplete")]
    public static class Patch_UnpaidDockingFees_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            string key = "UnpaidDockingFees.ObjectiveDescComplete";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(UnpauseWorld), "get_ObjectiveName")]
    public static class Patch_UnpauseWorld_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "UnpauseWorld.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }

    [HarmonyPatch(typeof(VisualisePower), "get_ObjectiveName")]
    public static class Patch_VisualisePower_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            string key = "VisualisePower.ObjectiveName";
            if (TranslationData.Objectives.TryGetValue(key, out string ru))
                __result = ru;
            return false;
        }
    }
    // ─── Translation data (loaded from translations.json) ───
    public static class TranslationData
    {
        internal static Dictionary<string, string> Hud = new Dictionary<string, string>(System.StringComparer.Ordinal);
        internal static Dictionary<string, string> Objectives = new Dictionary<string, string>(System.StringComparer.Ordinal);
        internal static Dictionary<string, string> Titles = new Dictionary<string, string>(System.StringComparer.Ordinal);
        internal static Dictionary<string, string> ReplacementsMfd = new Dictionary<string, string>(System.StringComparer.Ordinal);
        internal static Dictionary<string, string> ReplacementsGrammar = new Dictionary<string, string>(System.StringComparer.Ordinal);

        internal static void LoadFromJson(string path)
        {
            try
            {
                if (!System.IO.File.Exists(path))
                {
                    RuTranslation.Log?.LogError($"[Kaya] translations.json not found at {path}");
                    return;
                }
                string json = System.IO.File.ReadAllText(path, Encoding.UTF8);
                var root = JsonMapper.ToObject(json);

                Hud.Clear();
                Objectives.Clear();
                Titles.Clear();
                ReplacementsMfd.Clear();
                ReplacementsGrammar.Clear();

                LoadSection(root, "hud", Hud);
                LoadSection(root, "objectives", Objectives);
                LoadSection(root, "titles", Titles);
                LoadSection(root, "replacements_mfd", ReplacementsMfd);
                LoadSection(root, "replacements_grammar", ReplacementsGrammar);

                int total = Hud.Count + Objectives.Count + Titles.Count + ReplacementsMfd.Count + ReplacementsGrammar.Count;
                RuTranslation.Log?.LogInfo($"[Kaya] Loaded {total} translations ({Hud.Count} hud, {Objectives.Count} objectives, {Titles.Count} titles, {ReplacementsMfd.Count} mfd-repl, {ReplacementsGrammar.Count} grammar-repl)");
            }
            catch (System.Exception ex)
            {
                RuTranslation.Log?.LogError($"[Kaya] Failed to load translations.json: {ex}");
            }
        }

        private static void LoadSection(JsonData root, string key, Dictionary<string, string> dict)
        {
            if (!root.Keys.Contains(key)) return;
            var section = (JsonData)root[key];
            foreach (string k in section.Keys)
                dict[k] = (string)section[k];
        }
    }

    // ─── Universal HUD string replacement ───
    public static class HUDTranslation
    {
        internal static string TranslateString(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            // Exact match (trimmed for whitespace tolerance)
            if (TranslationData.Hud.TryGetValue(value, out string translated))
                return translated;
            string trimmed = value.Trim();
            if (trimmed != value && TranslationData.Hud.TryGetValue(trimmed, out string trimmedT))
                return trimmedT;
            // Partial replacement with WORD BOUNDARIES
            // Prevents "CLEAR" matching inside "CLEARANCE", "DOCK" inside "DOCKED" etc.
            foreach (var kvp in TranslationData.Hud)
            {
                if (kvp.Key.Length >= 2 && kvp.Value.Length > 0 && value.Contains(kvp.Key))
                {
                    // Use regex with word boundaries to avoid breaking compound words
                    string pattern = @"\b" + System.Text.RegularExpressions.Regex.Escape(kvp.Key) + @"\b";
                    value = System.Text.RegularExpressions.Regex.Replace(value, pattern, kvp.Value, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
            }
            return value;
        }

        internal static void TranslateList(List<string> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
                list[i] = TranslateString(list[i]);
        }

        internal static void ApplyMfdReplacements(List<string> list)
        {
            if (list == null) return;
            foreach (var kvp in TranslationData.ReplacementsMfd)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Contains(kvp.Key))
                        list[i] = list[i].Replace(kvp.Key, kvp.Value);
                }
            }
        }
    }

    // ─── MFD HUD: patch GUIMFDDisplay.ShowMenu ───
    // ShowMenu receives MFDDTO with PUBLIC Title/Left/Right fields.
    // We translate them in Prefix BEFORE ShowMenu reads them (ldfld)
    // and passes to Format()/FormatShort()/set_text().
    // This is the SINGLE entry point for ALL MFD rendering.
    [HarmonyPatch(typeof(Ostranauts.ShipGUIs.MFD.GUIMFDDisplay), "ShowMenu")]
    public static class Patch_GUIMFDDisplay_ShowMenu
    {
        static void Prefix(ref string id, ref Ostranauts.Events.DTOs.MFDDTO mfdDto)
        {
            if (mfdDto == null) return;
            try
            {
                if (!string.IsNullOrEmpty(mfdDto.Title))
                    mfdDto.Title = HUDTranslation.TranslateString(mfdDto.Title);

                if (mfdDto.Left != null)
                    for (int i = 0; i < mfdDto.Left.Count; i++)
                        mfdDto.Left[i] = HUDTranslation.TranslateString(mfdDto.Left[i]);

                if (mfdDto.Right != null)
                    for (int i = 0; i < mfdDto.Right.Count; i++)
                        mfdDto.Right[i] = HUDTranslation.TranslateString(mfdDto.Right[i]);
            }
            catch { }
        }
    }



    // Periodic scanner moved to RuTranslation.Update() — see below.
    // Was on GUIOrbitDraw.Update which only fires when orbit screen is open.

    // ─── Scan and translate all UI text after scene load ───
    [HarmonyPatch(typeof(GUIOrbitDraw), "Init")]
    public static class Patch_GUIOrbitDraw_Init_Translate
    {
        static void Postfix()
        {
            try
            {
                // Find ALL Text components in scene and translate
                var texts = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>();
                foreach (var t in texts)
                {
                    if (t != null && !string.IsNullOrEmpty(t.text))
                    {
                        string translated = HUDTranslation.TranslateString(t.text);
                        if (translated != t.text)
                            t.text = translated;
                    }
                }
                // Find ALL TMP_Text components
                var tmpTexts = UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>();
                foreach (var t in tmpTexts)
                {
                    if (t != null && !string.IsNullOrEmpty(t.text))
                    {
                        string translated = HUDTranslation.TranslateString(t.text);
                        if (translated != t.text)
                            t.text = translated;
                    }
                }
            }
            catch { }
        }
    }

    // Safety net: UI.Text.set_text for non-MFD UI
    [HarmonyPatch(typeof(UnityEngine.UI.Text), "set_text", typeof(string))]
    public static class Patch_UI_Text_SetText
    {
        static void Prefix(ref string value)
        {
            if (!string.IsNullOrEmpty(value))
                value = HUDTranslation.TranslateString(value);
        }
    }

    // Safety net: TMP_Text.set_text for NavMod/CrewSim/etc.
    [HarmonyPatch(typeof(TMPro.TMP_Text), "set_text", typeof(string))]
    public static class Patch_TMP_Text_SetText
    {
        static void Prefix(ref string value)
        {
            if (!string.IsNullOrEmpty(value))
                value = HUDTranslation.TranslateString(value);
        }
    }

    // ─── IMGUI hooks: GUILayout.DoLabel + GUI.DoLabel ───
    // NavMod panels (PLA, SIGNAL, ON, OFF, STATION KEEPING etc.) use IMGUI GUILayout.Label
    // which calls GUILayout.DoLabel(GUIContent, GUIStyle, GUILayoutOption[]).
    // We translate the GUIContent.text in Prefix before rendering.
    [HarmonyPatch(typeof(UnityEngine.GUILayout), "DoLabel")]
    public static class Patch_GUILayout_DoLabel
    {
        static void Prefix(ref UnityEngine.GUIContent content)
        {
            if (content != null && !string.IsNullOrEmpty(content.text))
            {
                string translated = HUDTranslation.TranslateString(content.text);
                if (translated != content.text)
                    content.text = translated;
            }
        }
    }

    [HarmonyPatch(typeof(UnityEngine.GUI), "DoLabel")]
    public static class Patch_GUI_DoLabel
    {
        static void Prefix(ref UnityEngine.GUIContent content)
        {
            if (content != null && !string.IsNullOrEmpty(content.text))
            {
                string translated = HUDTranslation.TranslateString(content.text);
                if (translated != content.text)
                    content.text = translated;
            }
        }
    }
}
