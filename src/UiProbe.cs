using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Boon
{
    /// <summary>
    /// A one-shot dump of the window we intend to clone.
    ///
    /// A UI hierarchy is scene data: ilspy names the fields but says nothing about the objects
    /// under them, and every name in a clone has to come from somewhere. Guessing them is what
    /// produced three misses on the borrowed skin, so this prints the real thing once and the
    /// clone gets written against it.
    ///
    /// SkillsDialog is the donor because it is a whole window rather than a piece of one: its
    /// own frame, title, scroll area and close behaviour, plus m_elementPrefab, a repeatable
    /// row the game itself stamps out per skill. That last part is what a hand-built panel
    /// cannot have - a row that is already the right font, the right height and the right
    /// hover, because the game made it.
    /// </summary>
    internal static class UiProbe
    {
        private static bool _done;

        internal static void Run()
        {
            if (_done || !BoonConfig.Verbose.Value) return;
            _done = true;

            var dialogs = Resources.FindObjectsOfTypeAll<SkillsDialog>();
            if (dialogs == null || dialogs.Length == 0)
            {
                BoonPlugin.Log.LogWarning("Probe: no SkillsDialog loaded - nothing to clone from.");
                return;
            }

            var dialog = dialogs[0];

            BoonPlugin.Log.LogInfo("Probe: SkillsDialog on '" + Path(dialog.transform) + "'");
            BoonPlugin.Log.LogInfo("Probe: window\n" + Tree(dialog.transform, 4));

            if (dialog.m_listRoot != null)
                BoonPlugin.Log.LogInfo("Probe: listRoot '" + Path(dialog.m_listRoot) + "' with " +
                                       dialog.m_listRoot.childCount + " children");

            if (dialog.m_elementPrefab != null)
                BoonPlugin.Log.LogInfo("Probe: element prefab\n" + Tree(dialog.m_elementPrefab.transform, 4));
            else
                BoonPlugin.Log.LogWarning("Probe: SkillsDialog has no element prefab.");
        }

        /// <summary>
        /// Name, components and the few properties that decide how a clone has to be driven -
        /// a TMP_Text's text, an Image's sprite, whether something is a Button. Depth-limited
        /// because a window's leaves run to hundreds of objects and only the shape matters.
        /// </summary>
        private static string Tree(Transform root, int depth)
        {
            var sb = new StringBuilder();
            Walk(root, 0, depth, sb);
            return sb.ToString();
        }

        private static void Walk(Transform t, int level, int max, StringBuilder sb)
        {
            if (t == null || level > max) return;

            sb.Append(' ', 2 + level * 2).Append(t.name);

            var parts = new List<string>();
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;

                var type = c.GetType().Name;
                if (type == "RectTransform" || type == "Transform" || type == "CanvasRenderer") continue;

                var tmp = c as TMPro.TMP_Text;
                if (tmp != null)
                {
                    var text = tmp.text ?? "";
                    if (text.Length > 24) text = text.Substring(0, 24) + "…";
                    parts.Add(type + "(\"" + text + "\" " + (int)tmp.fontSize + "px)");
                    continue;
                }

                var image = c as UnityEngine.UI.Image;
                if (image != null)
                {
                    parts.Add(type + "(" + (image.sprite != null ? image.sprite.name : "no sprite") + ")");
                    continue;
                }

                parts.Add(type);
            }

            if (parts.Count > 0) sb.Append("  [").Append(string.Join(", ", parts.ToArray())).Append(']');
            if (!t.gameObject.activeSelf) sb.Append("  (inactive)");

            sb.Append('\n');

            for (var i = 0; i < t.childCount; i++) Walk(t.GetChild(i), level + 1, max, sb);
        }

        private static string Path(Transform t)
        {
            var name = t.name;
            for (var p = t.parent; p != null; p = p.parent) name = p.name + "/" + name;
            return name;
        }
    }
}
