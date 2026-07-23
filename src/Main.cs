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
            if (__result == "MAIN MENU") __result = "ГЛАВ. МЕНЮ";
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
                {
                    MFDTranslate.ReplaceInList(left, "<LOCAL CHANNEL", "<ЛОК. КАНАЛ");
                    MFDTranslate.ReplaceInList(left, "<MESSAGE LOG", "<ЖУРН. СООБЩ.");
                    MFDTranslate.ReplaceInList(left, "ATC CHANNEL: ", "КАНАЛ АТС: ");
                    MFDTranslate.ReplaceInList(left, "DOCKED WITH: ", "ПРИСТЫКОВАН К: ");
                    MFDTranslate.ReplaceInList(left, "<DOCK INFO", "<ИНФ. О СТЫКОВКЕ");
                    MFDTranslate.ReplaceInList(left, "<UNREAD MESSAGES", "<НЕПР. СООБЩ.");
                }
                if (right != null)
                {
                    MFDTranslate.ReplaceInList(right, "HAIL SHIP>", "СВЯЗЬ>");
                }
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
                var leftField = AccessTools.Field(typeof(MFDPage), "_left");
                var left = leftField?.GetValue(__instance) as List<string>;
                if (left != null)
                {
                    MFDTranslate.ReplaceInList(left, "MOORED WITH ", "ШВАРТОВАН С ");
                    MFDTranslate.ReplaceInList(left, "DOCKED WITH ", "ПРИСТЫКОВАН С ");
                    MFDTranslate.ReplaceInList(left, "DOCK INFO", "ИНФО О СТЫКОВКЕ");
                    MFDTranslate.ReplaceInList(left, "REG ID", "РЕГ ID");
                    MFDTranslate.ReplaceInList(left, "RATING CODE", "КОД РЕЙТИНГА");
                    MFDTranslate.ReplaceInList(left, "RETURN TO", "ВЕРНУТЬСЯ В");
                    MFDTranslate.ReplaceInList(left, "MAIN MENU>", "ГЛАВ. МЕНЮ>");
                    MFDTranslate.ReplaceInList(left, "NO DOCKED VESSEL", "НЕТ ПРИСТЫКОВАННОГО СУДНА");
                }
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
            if (__result == "Recieved Messages:") __result = "Полученные сообщения:";
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
                {
                    MFDTranslate.ReplaceInList(__result, "RETURN TO", "ВЕРНУТЬСЯ В");
                    MFDTranslate.ReplaceInList(__result, "MAIN MENU>", "ГЛАВ. МЕНЮ>");
                }
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
            __result = "Взять скафандр и шлем";
            return false;
        }
    }

    [HarmonyPatch(typeof(CrowbarHallway3), "get_ObjectiveDescComplete")]
    public static class Patch_CrowbarHallway3_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            __result = "Любопыт. удовлетворено.";
            return false;
        }
    }

    [HarmonyPatch(typeof(CrowbarHallway4), "get_ObjectiveName")]
    public static class Patch_CrowbarHallway4_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Исследовать дальше";
            return false;
        }
    }

    [HarmonyPatch(typeof(CrowbarHallway5), "get_ObjectiveDescComplete")]
    public static class Patch_CrowbarHallway5_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            __result = "Гражданский долг выполнен.";
            return false;
        }
    }

    
    // REMOVED: DeployLeo_ObjectiveName (method does not exist)


    [HarmonyPatch(typeof(DismissNote), "get_ObjectiveName")]
    public static class Patch_DismissNote_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Убрать записку";
            return false;
        }
    }

    [HarmonyPatch(typeof(DismissNote), "get_ObjectiveDescComplete")]
    public static class Patch_DismissNote_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            __result = "Памятка убрана";
            return false;
        }
    }

    [HarmonyPatch(typeof(DockWithDerelict), "get_ObjectiveName")]
    public static class Patch_DockWithDerelict_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Стыковаться с брошенным кораблём";
            return false;
        }
    }

    [HarmonyPatch(typeof(DockWithDerelict), "get_ObjectiveDescComplete")]
    public static class Patch_DockWithDerelict_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            __result = "Успешная стыковка.";
            return false;
        }
    }

    
    // REMOVED: Dodge_ObjectiveName (method does not exist)


    [HarmonyPatch(typeof(ExpandMTT), "get_ObjectiveName")]
    public static class Patch_ExpandMTT_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Развернуть подсказку";
            return false;
        }
    }

    [HarmonyPatch(typeof(ExploreTutorialDerelict), "get_ObjectiveName")]
    public static class Patch_ExploreTutorialDerelict_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Исследовать брошенный корабль";
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
            __result = "Получить допуск к стыковке";
            return false;
        }
    }

    [HarmonyPatch(typeof(GainClearance), "get_ObjectiveDescComplete")]
    public static class Patch_GainClearance_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            __result = "Допуск к стыковке получен.";
            return false;
        }
    }

    [HarmonyPatch(typeof(HallwayConduit7), "get_ObjectiveName")]
    public static class Patch_HallwayConduit7_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Обыскать стойку";
            return false;
        }
    }

    [HarmonyPatch(typeof(HallwayConduit9), "get_ObjectiveName")]
    public static class Patch_HallwayConduit9_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Продолжить к своему кораблю";
            return false;
        }
    }

    [HarmonyPatch(typeof(HelmetAtmosphereUnsafe), "get_ObjectiveName")]
    public static class Patch_HelmetAtmosphereUnsafe_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Атмосфера в шлеме небезопасна";
            return false;
        }
    }

    [HarmonyPatch(typeof(MatchSpeed), "get_ObjectiveDescComplete")]
    public static class Patch_MatchSpeed_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            __result = "Синхр. скорости перекл.";
            return false;
        }
    }

    [HarmonyPatch(typeof(NavUseShow), "get_ObjectiveName")]
    public static class Patch_NavUseShow_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Исп. навиг. станцию";
            return false;
        }
    }

    [HarmonyPatch(typeof(NavWalk), "get_ObjectiveName")]
    public static class Patch_NavWalk_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Посетить свой корабль";
            return false;
        }
    }

    [HarmonyPatch(typeof(PayDockingFee), "get_ObjectiveName")]
    public static class Patch_PayDockingFee_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Оплатить стык. сбор";
            return false;
        }
    }

    [HarmonyPatch(typeof(PayDockingFee), "get_ObjectiveDescComplete")]
    public static class Patch_PayDockingFee_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            __result = "Стыковочные сборы оплачены.";
            return false;
        }
    }

    [HarmonyPatch(typeof(PrepareToExploreDerelict), "get_ObjectiveName")]
    public static class Patch_PrepareToExploreDerelict_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Надеть скафандр";
            return false;
        }
    }

    [HarmonyPatch(typeof(ReachBridgeTest), "get_ObjectiveName")]
    public static class Patch_ReachBridgeTest_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Доступ к мостику";
            return false;
        }
    }

    [HarmonyPatch(typeof(RefuelAtKiosk), "get_ObjectiveDescComplete")]
    public static class Patch_RefuelAtKiosk_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            __result = "Корабль заправлен.";
            return false;
        }
    }

    [HarmonyPatch(typeof(RequestClearance), "get_ObjectiveName")]
    public static class Patch_RequestClearance_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Запросить допуск к расстыковке";
            return false;
        }
    }

    [HarmonyPatch(typeof(RestoreNavStation), "get_ObjectiveName")]
    public static class Patch_RestoreNavStation_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Восстановить навиг. станцию";
            return false;
        }
    }

    [HarmonyPatch(typeof(RosterPermission), "get_ObjectiveName")]
    public static class Patch_RosterPermission_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Изменить права экипажа";
            return false;
        }
    }

    [HarmonyPatch(typeof(SelectCompartment), "get_ObjectiveName")]
    public static class Patch_SelectCompartment_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Проверить атмосферу отсека";
            return false;
        }
    }

    [HarmonyPatch(typeof(SellSalvageAtKiosk), "get_ObjectiveDescComplete")]
    public static class Patch_SellSalvageAtKiosk_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            __result = "Продажа утиля завершена.";
            return false;
        }
    }

    [HarmonyPatch(typeof(SwitchNav), "get_ObjectiveName")]
    public static class Patch_SwitchNav_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Перейти к экрану навигации";
            return false;
        }
    }

    [HarmonyPatch(typeof(SwitchNav), "get_ObjectiveDescComplete")]
    public static class Patch_SwitchNav_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            __result = "Перекл. на экран навигации.";
            return false;
        }
    }

    [HarmonyPatch(typeof(TargetDerelict), "get_ObjectiveName")]
    public static class Patch_TargetDerelict_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Цель: ближайший брошенный корабль";
            return false;
        }
    }

    [HarmonyPatch(typeof(TargetOKLG), "get_ObjectiveName")]
    public static class Patch_TargetOKLG_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Цель: станция K-LEG";
            return false;
        }
    }

    [HarmonyPatch(typeof(ToggleOffMatchSpeed), "get_ObjectiveDescComplete")]
    public static class Patch_ToggleOffMatchSpeed_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            __result = "Синхр. скорости перекл.";
            return false;
        }
    }

    [HarmonyPatch(typeof(TravelToDerelict), "get_ObjectiveName")]
    public static class Patch_TravelToDerelict_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Двигаться до зоны стыковки";
            return false;
        }
    }

    [HarmonyPatch(typeof(TutorialEnd), "get_ObjectiveName")]
    public static class Patch_TutorialEnd_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Обучение завершено";
            return false;
        }
    }

    [HarmonyPatch(typeof(TutorialStub), "get_ObjectiveName")]
    public static class Patch_TutorialStub_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Заглушка обучения";
            return false;
        }
    }

    [HarmonyPatch(typeof(UnpaidDockingFees), "get_ObjectiveName")]
    public static class Patch_UnpaidDockingFees_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Неоплаченные сборы";
            return false;
        }
    }

    [HarmonyPatch(typeof(UnpaidDockingFees), "get_ObjectiveDescComplete")]
    public static class Patch_UnpaidDockingFees_ObjectiveDescComplete
    {
        static bool Prefix(ref string __result)
        {
            __result = "Стыковочные сборы оплачены!";
            return false;
        }
    }

    [HarmonyPatch(typeof(UnpauseWorld), "get_ObjectiveName")]
    public static class Patch_UnpauseWorld_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Снять с паузы";
            return false;
        }
    }

    [HarmonyPatch(typeof(VisualisePower), "get_ObjectiveName")]
    public static class Patch_VisualisePower_ObjectiveName
    {
        static bool Prefix(ref string __result)
        {
            __result = "Визуализация энергосети";
            return false;
        }
    }
    // ─── Universal HUD string replacement ───
    public static class HUDTranslation
    {
        internal static readonly Dictionary<string, string> HudTranslations = new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            {"<APPLIED", "<ПРИМЕНЕНО"},
            {"<APPLY TO ALL", "<ПРИМ. КО ВСЕМ"},
            {"<BACK", "<НАЗАД"},
            {"<CYCLE PAGE", "<ЛИСТ. СТРАНИЦУ"},
            {"<DOCK INFO", "<ИНФ. О СТЫКОВКЕ"},
            {"<DOCKING", "<СТЫКОВКА"},
            {"<FIRING MODE:", "<РЕЖИМ ОГНЯ:"},
            {"<GROUP:", "<ГРУППА:"},
            {"<LOCAL CHANNEL", "<ЛОК. КАНАЛ"},
            {"<MAIN MENU", "<ГЛАВ. МЕНЮ"},
            {"<MESSAGE LOG", "<ЖУРН. СООБЩ."},
            {"<PREVIOUS PAGE", "<ПРЕД. СТРАНИЦА"},
            {"<REQUEST CLEARANCE", "<ЗАПР. ДОПУСК"},
            {"<SHOW ON NAV MAP", "<ПОКАЗ. НА КАРТЕ"},
            {"<STATUS:", "<СТАТУС:"},
            {"<TARGET SELECT:", "<ВЫБОР ЦЕЛИ:"},
            {"<UNMOOR", "<ОТШВАРТОВАТЬ"},
            {"<UNREAD MESSAGES", "<НЕПР. СООБЩ."},
            {"ACTIVE", "АКТИВЕН"},
            {"ACTIVE SENSORS:", "АКТИВ. СЕНСОРЫ:"},
            {"AFT", "КОРМА"},
            {"ALL CLEAR: Closest approach to", "ВСЕ ЧИСТО: Ближайшее сближение с"},
            {"ARS 2000 - Automated Response Service", "ARS 2000 - Автоответчик"},
            {"ATC CHANNEL:", "КАНАЛ АТС:"},
            {"ATC Regional Control -", "Региональный АТС -"},
            {"AUTO", "АВТО"},
            {"AUTOMATIC", "АВТОМАТИЧ."},
            {"Access The Bridge", "Доступ к мостику"},
            {"Adjust the NAV Station Zoom", "Настроить зум навиг. станции"},
            {"Age:", "Возраст:"},
            {"Allowed items: all", "Разреш. предметы: все"},
            {"Amount:", "Кол-во:"},
            {"Anchoring docked ship", "Якорь стыкованного судна"},
            {"Are you sure you want to quit to desktop?", "Вы уверены, что хотите выйти в Windows?"},
            {"Aux Dock ID:", "Доп. ID стыковки:"},
            {"BACKUP POWER:", "РЕЗЕРВ ПИТАНИЯ:"},
            {"BRG -", "ПЕЛЕНГ -"},
            {"Boarding", "Абордаж"},
            {"Body Temp", "Темп. тела"},
            {"Bribe Amount: $", "Размер взятки: $"},
            {"CAUTION: Older saves may experience problems.", "ВНИМАНИЕ: Старые сейвы могут иметь проблемы."},
            {"CITIZENSHIP VERIFIED", "ГРАЖДАНСТВО ПОДТВЕРЖДЕНО"},
            {"CLEARANCE AVAILABLE", "ДОПУСК ДОСТУПЕН"},
            {"CLEARED TO", "РАЗРЕШЕНО"},
            {"CLOSE INVENTORY", "ЗАКРЫТЬ ИНВЕНТАРЬ"},
            {"CONNECTED WITH -", "ПОДКЛЮЧЕНО К -"},
            {"Calculating target coordinates", "Расчёт координат цели"},
            {"Camera Controls", "Управление камерой"},
            {"Camera Down", "Камера вниз"},
            {"Camera Left", "Камера влево"},
            {"Camera Right", "Камера вправо"},
            {"Camera Target", "Камера: цель"},
            {"Camera Up", "Камера вверх"},
            {"Captain", "Капитан"},
            {"Change Roster Permissions", "Изменить права экипажа"},
            {"Cheat Sheet Dismissed", "Памятка убрана"},
            {"Check Room Atmosphere", "Проверить атмосферу отсека"},
            {"Civic duty performed.", "Гражданский долг выполнен."},
            {"Clearance:", "Допуск:"},
            {"Collect Pressure Suit and Helmet", "Взять скафандр и шлем"},
            {"Comms", "Связь"},
            {"Compartment", "Отсек"},
            {"Connected with", "Подключён к"},
            {"Connecting..", "Подключение.."},
            {"Container", "Контейнер"},
            {"Continue To Your Ship", "Продолжить к своему кораблю"},
            {"Cooling", "Охлаждение"},
            {"Course Vector", "Вектор курса"},
            {"Create New Child Node", "Создать дочерний узел"},
            {"Create a Zone", "Создать зону"},
            {"Crew", "Экипаж"},
            {"Crew Member Selected", "Выбран член экипажа"},
            {"Criminal", "Преступник"},
            {"Crippled", "Калека"},
            {"Curiosity sated.", "Любопыт. удовлетворено."},
            {"Current Preset:", "Текущий пресет:"},
            {"Cycle Crew", "Перекл. экипаж"},
            {"Cycle to New Crew", "Перекл. на нового члена экипажа"},
            {"DANGER:", "ОПАСНОСТЬ:"},
            {"DANGER: Atmospheric Entry with", "ОПАСНОСТЬ: Атмосферный вход с"},
            {"DANGER: Collision predicted with", "ОПАСНОСТЬ: Предсказано столкновение с"},
            {"DELTA-V:", "ДЕЛЬТА-V:"},
            {"DOCK", "СТЫКОВКА"},
            {"DOCK INFO", "ИНФО О СТЫКОВКЕ"},
            {"DOCKED WITH", "ПРИСТЫКОВАН С"},
            {"DOCKED WITH:", "ПРИСТЫКОВАН С:"},
            {"DOCKING MESSAGE -", "СТЫКОВ. СООБЩ. -"},
            {"Danger", "Опасность"},
            {"Dead", "Мёртв"},
            {"Death Pay: $", "Выплата за гибель: $"},
            {"Death Report", "Отчёт о гибели"},
            {"Delete JSON Entry", "Удалить запись JSON"},
            {"Deploying Hauler", "Развернуть буксир"},
            {"Deploying Law Enforcement Officer", "Развернуть полицию"},
            {"Designation:", "Обозначение:"},
            {"Died", "Погиб"},
            {"Dimensions:", "Размеры:"},
            {"Dismiss Note", "Убрать записку"},
            {"Distance to plotted destination:", "Дистанция до пункта назначения:"},
            {"Dock with Derelict", "Стыковаться с брошенным кораблём"},
            {"Docking", "Стыковка"},
            {"Docking fees paid!", "Стыковочные сборы оплачены!"},
            {"Docking fees paid.", "Стыковочные сборы оплачены."},
            {"Docking procedure aborted: target airlock is not safe.", "Стыковка отменена: шлюз цели небезопасен."},
            {"Dodging incoming", "Уклонение от угрозы"},
            {"Duplicate Node", "Дублировать узел"},
            {"Duties", "Обязанности"},
            {"ETA", "расч. время"},
            {"ETA -", "РВ -"},
            {"Estimated value: $", "Оценочная стоимость: $"},
            {"Expand Mega Tooltip", "Развернуть подсказку"},
            {"Explore Derelict", "Исследовать брошенный корабль"},
            {"FFWD", "УСКОР."},
            {"FORWARD", "НОС"},
            {"FUEL:", "ТОПЛИВО:"},
            {"FUNDS VERIFIED", "СРЕДСТВА ПОДТВЕРЖДЕНЫ"},
            {"Firefight", "Перестрелка"},
            {"Flotilla", "Флотилия"},
            {"Flying to", "Лёт к"},
            {"Friendly:", "Союзники:"},
            {"Fuel request was sent", "Запр. на топливо отправлен"},
            {"Fuel transfer complete", "Передача топлива завершена"},
            {"Gain Clearance to Dock", "Получить допуск к стыковке"},
            {"Gained Docking Clearance.", "Допуск к стыковке получен."},
            {"HAIL SHIP>", "СВЯЗЬ>"},
            {"HOLD BUTTON FOR WEAPON MENU", "УДЕРЖИВАТЬ ДЛЯ МЕНЮ ОРУЖИЯ"},
            {"HULL", "КОРПУС"},
            {"Hail", "Вызов"},
            {"Hail Ship", "Вызвать корабль"},
            {"Heat", "Нагрев"},
            {"Heating", "Нагревается"},
            {"Helmet Atmosphere Unsafe", "Атмосфера в шлеме небезопасна"},
            {"Hold", "Удержание"},
            {"Holding position", "Удерживание позиции"},
            {"Homeport:", "Порт приписки:"},
            {"INACTIVE", "НЕАКТИВЕН"},
            {"Investigate Further", "Исследовать дальше"},
            {"LIFE SUPPORT COOL:", "ЖИЗН. ОБЕСП. ОХЛАЖД.:"},
            {"LIFE SUPPORT HEAT:", "ЖИЗН. ОБЕСП. НАГРЕВ:"},
            {"LIFE SUPPORT O2 STORES:", "ЖИЗН. ОБЕСП. ЗАПАС O2:"},
            {"LIFE SUPPORT WORKING O2 PUMPS:", "ЖИЗН. ОБЕСП. РАБОТАЮЩ. НАСОСЫ O2:"},
            {"LOCK", "ЗАХВАТ"},
            {"LOCKING", "ЗАХВАТ..."},
            {"Launchers:", "Пусковые:"},
            {"MAIN MENU", "ГЛАВ. МЕНЮ"},
            {"MAIN MENU>", "ГЛАВ. МЕНЮ>"},
            {"MOORED WITH", "ШВАРТОВАН С"},
            {"NAV MODE:", "РЕЖИМ НАВИГ.:"},
            {"NAV STATION:", "НАВИГ. СТАНЦИЯ:"},
            {"Nav", "Навигация"},
            {"OFFLINE", "ОФЛАЙН"},
            {"ONLINE", "ОНЛАЙН"},
            {"OPEN CHANNEL TO", "ОТКР. КАНАЛ К"},
            {"Orbit", "Орбита"},
            {"PASSIVE SENSORS:", "ПАССИВ. ДАТЧИКИ:"},
            {"PORT", "ЛЕВЫЙ БОРТ"},
            {"Patrolling assigned sector", "Патрулирование сектора"},
            {"Pay Docking Fee", "Оплатить стык. сбор"},
            {"Pilot", "Пилот"},
            {"Plot Manager Settings", "Настройки менеджера сюжетов"},
            {"Point of Ref:", "Точка отсчёта:"},
            {"Port ID:", "ID порта:"},
            {"Port#", "Порт#"},
            {"Power Overlay", "Слой питания"},
            {"Primary Dock ID:", "Осн. ID стыковки:"},
            {"Put on Pressure Suit", "Надеть скафандр"},
            {"RATING CODE", "КОД РЕЙТИНГА"},
            {"RCS Count:", "Кол-во RCS:"},
            {"RCS DISTRIBUTOR:", "RCS РАСПРЕД.:"},
            {"RCS REMASS:", "RCS РЕАКЦ. МАССА:"},
            {"RCS THRUSTERS:", "RCS ДВИГАТЕЛИ:"},
            {"REACTOR D2O:", "РЕАКТОР D2O:"},
            {"REACTOR HE3:", "РЕАКТОР He3:"},
            {"REACTOR:", "РЕАКТОР:"},
            {"READY", "ГОТОВ"},
            {"REG ID", "РЕГ ID"},
            {"Radar", "Радар"},
            {"Recieved Messages:", "Полученные сообщения:"},
            {"Refueling", "Заправка"},
            {"Remember to turn off match speed before moving the ship again.", "Не забудьте выключить синхронизацию скорости перед движением."},
            {"Repair", "Ремонт"},
            {"Request Undocking Clearance", "Запр. допуск к расстыковке"},
            {"Responding ships:", "Отвечающие корабли:"},
            {"Restore Nav Station", "Восстановить навиг. станцию"},
            {"Roster", "Список экипажа"},
            {"STARBOARD", "ПРАВЫЙ БОРТ"},
            {"Salvage", "Утиль"},
            {"Salvage sale complete.", "Продажа утиля завершена."},
            {"Save Node Changes", "Сохранить изменения узла"},
            {"Save Nodes", "Сохранить узлы"},
            {"Search The Rack", "Обыскать стойку"},
            {"Security Station", "Охранный пост"},
            {"Sensor", "Датчик"},
            {"Sensors", "Датчики"},
            {"Ship", "Корабль"},
            {"Ship destroyed", "Корабль уничтожен"},
            {"Ship refueled", "Корабль заправлен"},
            {"Ship refueled.", "Корабль заправлен."},
            {"Ship successfully departed", "Корабль успешно убыл"},
            {"Signal:", "Сигнал:"},
            {"Station", "Станция"},
            {"Station Keeping", "Удержание позиции"},
            {"Status", "Статус"},
            {"Successfully Docked.", "Успешная стыковка."},
            {"Switch Control Panels (Bottom)", "Сменить панель управления (Низ)"},
            {"Switch Control Panels (Left)", "Сменить панель управления (Лево)"},
            {"Switch Control Panels (Right)", "Сменить панель управления (Право)"},
            {"Switch Control Panels (Top)", "Сменить панель управления (Верх)"},
            {"Switch to Nav Screen", "Перейти к экрану навигации"},
            {"Switched to Nav Screen.", "Перекл. на экран навигации."},
            {"TOGGLE MODES", "ПЕРЕКЛ. РЕЖ."},
            {"TRANSPONDER ANTENNA:", "АНТЕННА ТРАНСПОНДЕРА:"},
            {"TRANSPONDER:", "ТРАНСПОНДЕР:"},
            {"Take Ship", "Занять корабль"},
            {"Target K-Leg Station", "Цель: станция K-LEG"},
            {"Target the closest Derelict", "Цель: ближайший брошенный корабль"},
            {"Target:", "Цель:"},
            {"Targeting", "Наведение"},
            {"They See Us As:", "Они видят нас как:"},
            {"Thrust Down", "Тяга вниз"},
            {"Thrust Left", "Тяга влево"},
            {"Thrust Right", "Тяга вправо"},
            {"Thrust Up", "Тяга вверх"},
            {"Toggle PDA Power Vizor", "Перекл. энерго-визор КПК"},
            {"Toggle Power UI", "Перекл. энерго-интерфейс"},
            {"Toggle station keeping", "Перекл. удержание позиции"},
            {"Toggle zone UI", "Перекл. интерфейс зон"},
            {"Toggled match speed.", "Синхр. скорости перекл."},
            {"Torch Drive:", "Факельный движок:"},
            {"Total Mass:", "Общая масса:"},
            {"Tracking", "Слежение"},
            {"Transit", "Транзит"},
            {"Travel to Docking Range", "Двигаться до зоны стыковки"},
            {"Trickle Charge", "Капельная зарядка"},
            {"Turn CCW", "Пов. прот. часовой"},
            {"Turn CW", "Пов. по часовой"},
            {"Tutorial Complete", "Обучение завершено"},
            {"Tutorial Stub", "Заглушка обучения"},
            {"UNKNOWN_STRING", "НЕИЗВЕСТНО"},
            {"Unconscious", "Без сознания"},
            {"Undo Last", "Отменить посл."},
            {"Undocking", "Отстыковка"},
            {"Unlicensed", "Без лицензии"},
            {"Unpaid Docking Fees", "Неоплаченные сборы"},
            {"Unpause World", "Снять с паузы"},
            {"Unpowered devices", "Обесточ. устройства"},
            {"Use Nav Station", "Исп. навиг. станцию"},
            {"Use the Nav Station", "Исп. навиг. станцию"},
            {"VESSEL MASS:", "МАССА СУДНА:"},
            {"VESSEL RATING CODE:", "КОД РЕЙТИНГА СУДНА:"},
            {"VISIT STEAM", "ПОСЕТИТЬ STEAM"},
            {"VREL -", "ОТН.СКОР. -"},
            {"VREL:", "ОТН.СКОР.:"},
            {"VX:", "БОК. СКОР:"},
            {"Value:", "Значение:"},
            {"Vessel Name:", "Имя судна:"},
            {"Visit OKLG Commercial Port Authority", "Посетить порт. администрацию OKLG"},
            {"Visit Your Ship", "Посетить свой корабль"},
            {"Visualize Power Networks", "Визуализация энергосети"},
            {"WARNING:", "ВНИМАНИЕ:"},
            {"WARNING: Fusion reactor damage!", "ВНИМАНИЕ: Повреждение реактора!"},
            {"WARNING: Massive X Impulse!", "ВНИМАНИЕ: Мощный X-импульс!"},
            {"WARNING: Massive Y Impulse!", "ВНИМАНИЕ: Мощный Y-импульс!"},
            {"Waiting for response", "Ожидание ответа"},
            {"Walk through door.", "Пройти через дверь."},
            {"Waypoint", "Путевая точка"},
            {"We See Them As:", "Мы видим их как:"},
            {"Weapons: ?", "Оружие: ?"},
            {"Welcome back, Captain.", "С возвращением, Капитан."},
            {"Welcome, Captain.", "Добро пожаловать, Капитан."},
            {"Year:", "Год:"},
            {"Your standing with", "Ваше отношение с"},
            {"ZOOM RANGE:", "ДИАП. ЗУМА:"},
            {"Zone Subtract", "Уменьшить зону"},
            {"Zoom Camera In", "Приблизить камеру"},
            {"Zoom Camera Out", "Отдалить камеру"},
            {"public ATC channel", "публичный канал АТС"},
        };

        internal static string TranslateString(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (HudTranslations.TryGetValue(value, out string translated))
                return translated;
            // Partial replacement for strings embedded in longer text
            foreach (var kvp in HudTranslations)
            {
                if (kvp.Key.Length >= 4 && value.Contains(kvp.Key))
                    value = value.Replace(kvp.Key, kvp.Value);
            }
            return value;
        }

        internal static void TranslateList(List<string> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                list[i] = TranslateString(list[i]);
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
}
