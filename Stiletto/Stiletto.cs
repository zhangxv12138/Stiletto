﻿using BepInEx;
using BepInEx.Configuration;
using BepInEx.Harmony;
using HarmonyLib;
using Studio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using static ChaFileDefine;

namespace Stiletto
{
    [BepInPlugin(GUID, nameof(Stiletto), "1.4")]
    public class Stiletto : BaseUnityPlugin
    {
        private const string CONFIG_PATH = "BepInEx/Stiletto";
        private const string FLAG_PATH = CONFIG_PATH + "/_flags.txt";
        private const string GUID = "com.essu.stiletto";
        private static int GUID_HASH = GUID.GetHashCode();

        private static Harmony hi;
        private ConfigEntry<KeyboardShortcut> toggleGuiKey { get; set; }

        private Rect windowRect = new Rect(200, 200, 220, 120);
        private GUILayoutOption glo_width20 = GUILayout.Width(20);

        internal static bool PLUGIN_ACTIVE = true;
        internal static bool GUI_ACTIVE = false;

        internal static readonly bool InStudio = Paths.ProcessName == "CharaStudio";

        private static Dictionary<string, HeelFlags> dictAnimFlags = new Dictionary<string, HeelFlags>();
        private static ConcurrentList<HeelInfo> heelInfos = new ConcurrentList<HeelInfo>();
        private static int _heelIndex = -1;
        private static int heelIndex
        {
            get => _heelIndex;
            set
            {
                if (_heelIndex != value)
                {
                    _heelIndex = value;
                    HeelIndexChanged();
                }
            }
        }

        internal Stiletto()
        {
            var di = new DirectoryInfo(CONFIG_PATH);
            if (!di.Exists) di.Create();

            //PLUGIN_ACTIVE = (Application.productName == "Koikatu");
            if (!PLUGIN_ACTIVE) return;

            ReloadConfig();

            toggleGuiKey = Config.AddSetting("Hotkeys", "GUI Toggle", new KeyboardShortcut(KeyCode.RightShift), "Toggles stiletto UI");

            hi = new Harmony(nameof(Stiletto));
            HarmonyWrapper.PatchAll(typeof(Stiletto), hi);

            // For hot reload.
            int v = (int)ClothesKind.shoes_outer;
            foreach (var cc in GameObject.FindObjectsOfType<ChaControl>())
            {
                foreach (var comp in cc.GetComponents<Component>())
                    if (comp.GetType().Name == nameof(HeelInfo))
                        DestroyImmediate(comp);

                ChangeCustomClothesHook(cc, ref v);
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(OCIChar), "ActiveKinematicMode")]
        public static void OCIChar_ActiveKinematicModeHook(OCIChar __instance)
        {
            if (heelIndex == -1 || heelInfos.Count == 0) return;
            foreach (var cc in heelInfos.Select(x => x.cc).ToArray()) LoadHeelFile(cc);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(ChaControl), "SetClothesState")]
        public static void ChaControl_SetClothesStateHook(ChaControl __instance, ref int clothesKind)
        {
            var ck = (ClothesKind)clothesKind;
            if (ck == ClothesKind.shoes_inner || ck == ClothesKind.shoes_outer)
                LoadHeelFile(__instance);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(YS_Assist), "SetActiveControl", new[] { typeof(GameObject), typeof(bool[]) })]
        public static void YS_Assist_SetActiveControl(ref bool __result, ref GameObject obj, ref bool[] flags)
        {
            if (__result)
                if (obj && obj.activeSelf)
                    if (flags != null && flags.Length > 1 && flags[2])
                    {
                        var ind = -1;
                        if (obj.name == "ct_shoes_inner") ind = (int)ClothesKind.shoes_inner;
                        if (obj.name == "ct_shoes_outer") ind = (int)ClothesKind.shoes_outer;
                        if (ind != -1)
                        {
                            var obj_nonRef = obj;
                            var ccs = GameObject.FindObjectsOfType<ChaControl>().Where(x => x.objClothes[ind] == obj_nonRef);
                            if (ccs.Any()) ChangeCustomClothesHook(ccs.First(), ref ind);
                        }
                    }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(ChaFileStatus), "set_shoesType")]
        public static void ChaFileStatus_set_shoesTypeHook(ChaFileStatus __instance)
        {
            var cc = FindObjectsOfType<ChaControl>().Where(x => x?.chaFile?.status == __instance).FirstOrDefault();
            if (cc == null) return;
            var ind = cc.fileStatus.shoesType == 0 ? 8 : 7;
            ChangeCustomClothesHook(cc, ref ind);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(ChaControl), "ChangeCustomClothes")]
        public static void ChangeCustomClothesHook(ChaControl __instance, ref int kind)
        {
            var ck = (ClothesKind)kind;
            if (ck == ClothesKind.shoes_inner || ck == ClothesKind.shoes_outer)
                LoadHeelFile(__instance);
        }

        internal static void SaveHeelFile(HeelInfo hi)
        {
            var configFile = $"{CONFIG_PATH}/{hi.heelName}.txt";
            File.WriteAllText(configFile, $"angleAnkle={hi.angleA.eulerAngles.x}\r\nangleLeg={hi.angleLeg.eulerAngles.x}\r\nheight={hi.height.y}\r\naughStuff={(hi.aughStuff ? 1 : 0)}\r\n");
        }

        internal static void LoadHeelFile(ChaControl __instance)
        {
            if (__instance == null) return;
            var fs = __instance.fileStatus;
            if (fs == null) return;

            var currentShoes = (int)(fs.shoesType == 0 ? ClothesKind.shoes_inner : ClothesKind.shoes_outer);
            var ic = __instance.infoClothes;
            if (ic == null || currentShoes > ic.Length) return;
            var fileName = ic[currentShoes]?.Name;
            if (fileName == null) return;

            var configFile = $"{CONFIG_PATH}/{fileName}.txt";

            float angleAnkle = 0f;
            float angleLeg = 0f;
            float height = 0f;
            bool aughStuff = false;

            if (!File.Exists(configFile))
            {
                File.WriteAllText(configFile, $"{nameof(angleAnkle)}=0\r\n{nameof(angleLeg)}=0\r\n{nameof(height)}=0\r\n");
            }
            var lines = File.ReadAllLines(configFile).Select(x => x.Split('='));
            foreach (var line in lines)
            {
                if (line.Length != 2) continue;
                switch (line[0].Trim())
                {
                    case nameof(angleAnkle):
                        float.TryParse(line[1], out angleAnkle);
                        break;
                    case nameof(angleLeg):
                        float.TryParse(line[1], out angleLeg);
                        break;
                    case nameof(height):
                        float.TryParse(line[1], out height);
                        break;
                    case nameof(aughStuff):
                        int.TryParse(line[1], out int augh);
                        aughStuff = augh == 1;
                        break;
                }
            }

            var heelInfo = __instance.gameObject.GetOrAddComponent<HeelInfo>();
            heelInfo.Setup(fileName, __instance, height, angleAnkle, angleLeg, aughStuff);
            heightBuffer = height.ToString("F3");
        }

        internal static HeelFlags FetchFlags(string name)
        {
            if (dictAnimFlags.TryGetValue(name, out HeelFlags hf)) return hf;
            hf = new HeelFlags();
            dictAnimFlags[name] = hf;
            SaveHeelFlags();
            return hf;
        }

        internal static void SaveHeelFlags()
        {
            File.WriteAllLines(FLAG_PATH, dictAnimFlags.Keys.OrderBy(x => x).Select(x => $"{x}={dictAnimFlags[x]}").ToArray());
        }

        void OnDestroy()
        {
            // For hot reload.
            foreach (var cc in GameObject.FindObjectsOfType<ChaControl>())
                GameObject.DestroyImmediate(cc.GetComponent<HeelInfo>());

            hi.UnpatchAll(nameof(Stiletto));
        }

        internal void Update()
        {
            if (PLUGIN_ACTIVE)
                if (toggleGuiKey.Value.IsDown())
                    GUI_ACTIVE = !GUI_ACTIVE;
        }

        internal void ReloadConfig()
        {
            if (File.Exists(FLAG_PATH))
            {
                foreach (var l in File.ReadAllLines(FLAG_PATH).Where(x => !x.StartsWith(";")))
                {
                    var args = l.Split('=');
                    if (args.Length == 2)
                    {
                        var name = args[0].Trim();
                        var flags = args[1].Trim();
                        dictAnimFlags[name] = HeelFlags.Parse(flags);
                    }
                }
            }
        }

        internal static void RegisterHeelInfo(HeelInfo hi)
        {
            heelInfos.Add(hi);
            if (heelIndex == -1) heelIndex = 0;
        }

        internal static void UnregisterHeelInfo(HeelInfo hi)
        {
            heelInfos.Remove(hi);
            if (heelInfos.Count == 0) heelIndex = -1;
        }

        internal void OnGUI()
        {
            if (GUI_ACTIVE)
            {
                windowRect = GUILayout.Window(GUID_HASH, windowRect, Window, "Stiletto");
            }
        }

        private static string heightBuffer = "";

        private static void HeelIndexChanged()
        {
            if (heelIndex == -1 || heelInfos.Count == 0) heightBuffer = "0.000";
            else heightBuffer = heelInfos[heelIndex].height.y.ToString("F3");
        }

        internal void Window(int id)
        {
            int count;
            if (heelIndex == -1 || (count = heelInfos.Count) == 0)
            {
                GUILayout.Label("No characters detected.");
                GUI.DragWindow();
                return;
            }

            var selected = heelInfos[heelIndex];
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUI.enabled = heelIndex > 0;
            if (GUILayout.Button("<", glo_width20))
            {
                heelIndex = Math.Max(0, heelIndex - 1);
                GUI.changed = false;
            }

            GUI.enabled = true;
            GUILayout.Label(selected.cc.fileParam.fullname);

            GUI.enabled = heelIndex < count - 1;
            if (GUILayout.Button(">", glo_width20))
            {
                heelIndex = Math.Min(heelIndex + 1, count - 1);
                GUI.changed = false;
            }
            GUI.enabled = true;

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Path:");
            GUILayout.Label(selected.pathName);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Anim:");
            GUILayout.Label(selected.animationName);
            GUILayout.EndHorizontal();



            selected.flags.ACTIVE = GUILayout.Toggle(selected.flags.ACTIVE, "Active");
            selected.flags.HEIGHT = GUILayout.Toggle(selected.flags.HEIGHT, "Adjust Height");
            selected.flags.TOE_ROLL = GUILayout.Toggle(selected.flags.TOE_ROLL, "Toe Roll");
            selected.flags.ANKLE_ROLL = GUILayout.Toggle(selected.flags.ANKLE_ROLL, "Ankle Roll");
            selected.flags.KNEE_BEND = GUILayout.Toggle(selected.flags.KNEE_BEND, "Knee Bend");


            if (GUILayout.Button("Save Flags"))
            {
                SaveHeelFlags();
                GUI.changed = false;
            }


            if (GUI.changed)
            {
                dictAnimFlags[selected.key] = selected.flags;
                //heelInfos[heelIndex] = selected;
            }
            GUI.changed = false;



            GUILayout.BeginHorizontal();
            GUILayout.Label("Shoes:");
            GUILayout.Label(selected.heelName);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Angle Ankle:");
            var angleA = GUILayout.TextField(selected.angleA.eulerAngles.x.ToString("F0"));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Angle Leg:");
            var angleLeg = GUILayout.TextField(selected.angleLeg.eulerAngles.x.ToString("F0"));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Height:");
            heightBuffer = GUILayout.TextField(heightBuffer);
            //var height = GUILayout.TextField(selected.height.y.ToString("F3"));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Augh:");
            var b_aughStuff = GUILayout.Toggle(selected.aughStuff, "");
            //var height = GUILayout.TextField(selected.height.y.ToString("F3"));
            GUILayout.EndHorizontal();
            if (GUI.changed)
            {
                if (angleA.Length == 0) angleA = "0";
                if (angleLeg.Length == 0) angleLeg = "0";
                if (heightBuffer.Length == 0) heightBuffer = "0";
                if (
                    float.TryParse(angleA, out float f_angleA) &&
                    float.TryParse(angleLeg, out float f_angleLeg) &&
                    float.TryParse(heightBuffer, out float f_height)
                )
                {
                    selected.UpdateValues(f_height, f_angleA, f_angleLeg, b_aughStuff);
                }
                //heelInfos[heelIndex] = selected;
            }
            if (GUILayout.Button("Save Preset"))
            {
                SaveHeelFile(selected);
                foreach (var cc in heelInfos.Select(x => x.cc)) LoadHeelFile(cc);
            }

            GUILayout.EndVertical();

            GUI.DragWindow();
        }
    }
}