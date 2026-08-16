using UnityEngine;

namespace Boon
{
    /// <summary>
    /// The game's own typefaces, borrowed.
    ///
    /// This was once a whole borrowed skin - wood panel, slot, selection frame, separator,
    /// bar track - baked out of the game's atlases so an IMGUI window could pretend to be one
    /// of Valheim's. It never convinced anyone: IMGUI cannot draw with the game's shaders, so
    /// every copied sprite had to survive a colour space round trip that was guessed wrong
    /// three times, and even correct it read as an imitation of a window rather than a window.
    ///
    /// The panel is runestones now, which imitate nothing, and all that borrowing went with
    /// it. What is left is the part that always worked: the fonts. Nothing is loaded from
    /// disk - every font the game built is already in memory, and Resources.FindObjectsOfTypeAll
    /// reaches them whether or not anything is showing them.
    /// </summary>
    internal static class Skin
    {
        /// <summary>Body text. AveriaSerifLibre is what the game sets nearly everything in.</summary>
        internal static Font Face;

        /// <summary>Headings and names, in the same family a weight heavier.</summary>
        internal static Font HeadFace;

        /// <summary>
        /// The rune face, which is what makes a carved mark free: an inscription is text.
        ///
        /// It is Latin-mapped - type F, get the rune - so the marks and sigils are written as
        /// Latin letters. Runic code points come out of it as empty boxes, which is a whole
        /// screen of tofu squares learned the hard way.
        /// </summary>
        internal static Font RuneFace;

        private static bool _tried;

        private static readonly string[] FaceNames = { "AveriaSerifLibre-Regular", "AveriaSerifLibre-Light" };
        private static readonly string[] HeadFaceNames = { "AveriaSerifLibre-Bold", "Norsebold", "Norse" };
        private static readonly string[] RuneFaceNames = { "rune", "Norsebold", "Norse" };

        internal static void Ensure()
        {
            if (_tried) return;
            _tried = true;

            Face = FindFace(FaceNames);
            HeadFace = FindFace(HeadFaceNames) ?? Face;
            RuneFace = FindFace(RuneFaceNames) ?? HeadFace;

            BoonPlugin.Log.LogInfo("Fonts: body=" + Name(Face) + ", heading=" + Name(HeadFace) +
                                   ", rune=" + Name(RuneFace) + ".");
        }

        private static string Name(Font font)
        {
            return font != null ? font.name : "default";
        }

        /// <summary>
        /// A real Font, not a TMP_FontAsset - IMGUI cannot use the latter. The game ships both,
        /// which is the only reason any of this works without shipping a font file.
        /// </summary>
        private static Font FindFace(string[] preferred)
        {
            var all = Resources.FindObjectsOfTypeAll<Font>();

            foreach (var name in preferred)
            {
                foreach (var font in all)
                {
                    if (font != null && font.name == name) return font;
                }
            }

            // Anything that is not the default is still closer than the default.
            foreach (var font in all)
            {
                if (font == null || font.name == null) continue;
                if (font.name.StartsWith("Arial") || font.name.StartsWith("Liberation")) continue;
                return font;
            }

            return null;
        }
    }
}
