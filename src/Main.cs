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

        private void Awake()
        {
            try
            {
                Log = Logger;
                Log.LogInfo("[Kaya] Plugin starting...");

                LoadConjugations();
                LoadTutorialTranslations();
                ReplaceGrammarDictionaries();
                Log.LogInfo("[Kaya] Grammar replaced");

                try
                {
                    var harmony = new Harmony("ru.kaya.ostranautsrukaya");
                    harmony.PatchAll();
                    Log.LogInfo("[Kaya] Harmony patches applied");
                }
                catch (Exception ex)
                {
                    Log.LogError($"[Kaya] Harmony patches failed: {ex}");
                }

                Log.LogInfo("[Kaya] Ru Translation loaded");
            }
            catch (Exception ex)
            {
                Log?.LogError($"[Kaya] Awake failed: {ex}");
            }
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
                strMsg = strMsg.Replace(" are no longer ", " больше не ");
                strMsg = strMsg.Replace(" are ", " ");
            }
        }
    }
}
