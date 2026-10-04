using System.Collections.Generic;
using UnityEngine;

namespace Atlas.Rendering
{
    /// <summary>
    /// The material a panel within reach of the hands draws its text with (documents/ATLAS_Hands_Design.md
    /// section 6.1). The panels' text is legacy 3D text on the font's own material, whose built-in shader
    /// passes the depth test always: on the world layer, where a near panel stands, it would paint over a
    /// finger held in front of it. Atlas/NearText is that shader with the depth test on. There is one material
    /// per font, made once, carrying the font's own texture; a view gives it to its text renderers while it is
    /// within reach and gives the font's material back when it is not.
    /// </summary>
    public static class NearText
    {
        /// <summary>The shader's name under Resources, where a player build finds it with nothing referencing it.</summary>
        public const string ShaderResource = "AtlasNearText";
        /// <summary>The shader's own name, as a material reports it.</summary>
        public const string ShaderName = "Atlas/NearText";

        // One entry per font: the app has one, the built-in font of the menu and the signs.
        static readonly List<Font> s_fonts = new List<Font>(2);
        static readonly List<Material> s_materials = new List<Material>(2);
        static bool s_listening, s_missingLogged;

        /// <summary>
        /// The depth-tested material of a font, with the font's texture of this moment. Null when there is no
        /// font, or when the shader is not in the build: the view then keeps the font's own material, and its
        /// text stays readable though it does not go behind a finger.
        /// </summary>
        public static Material For(Font font)
        {
            if (font == null) return null;
            int at = s_fonts.IndexOf(font);
            if (at < 0 || s_materials[at] == null)
            {
                Shader shader = Resources.Load<Shader>(ShaderResource);
                if (shader == null)
                {
                    if (!s_missingLogged) Debug.LogWarning("Atlas: shader '" + ShaderResource + "' is not in Resources; near text is not depth-tested.");
                    s_missingLogged = true;
                    return null;
                }
                var material = new Material(shader) { name = "Atlas Near Text", hideFlags = HideFlags.HideAndDontSave };
                if (at < 0)
                {
                    at = s_fonts.Count;
                    s_fonts.Add(font);
                    s_materials.Add(material);
                }
                else s_materials[at] = material;
                if (!s_listening)
                {
                    s_listening = true;
                    Font.textureRebuilt += Rebind;
                }
            }
            Bind(font, s_materials[at]);
            return s_materials[at];
        }

        // A dynamic font grows its atlas when new characters are asked for, and Unity does not promise that the
        // texture object survives that: the material follows the font's own.
        static void Rebind(Font font)
        {
            int at = s_fonts.IndexOf(font);
            if (at >= 0 && s_materials[at] != null) Bind(font, s_materials[at]);
        }

        static void Bind(Font font, Material material)
        {
            Material source = font.material;
            if (source == null) return;
            material.mainTexture = source.mainTexture;
            if (source.HasProperty("_Color")) material.color = source.color;
        }
    }
}
