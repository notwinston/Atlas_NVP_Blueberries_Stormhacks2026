#if !ATLAS_HEADLESS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.TestTools;

namespace Atlas.Rendering.Tests.Look
{
    /// <summary>
    /// Compiles every pass of every shader under Assets/Atlas for the Editor's graphics API. Unity compiles a
    /// shader only when something draws with it, so a shader no test draws (the Bump Guard cue, the flat twin)
    /// can carry a compile error into a build unseen.
    /// </summary>
    [Category("Unity")]
    public class ShaderCompileTests
    {
        static readonly string[] Roots = { "Assets/Atlas" };

        [Test]
        public void EveryAtlasShader_CompilesEveryPass()
        {
            var errors = new List<string>();
            bool ignored = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;   // the compiler also logs each error; they are collected below
            try
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Shader", Roots))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                    if (shader == null) { errors.Add(path + ": did not import"); continue; }
                    var material = new Material(shader);
                    try
                    {
                        for (int pass = 0; pass < material.passCount; pass++)
                            ShaderUtil.CompilePass(material, pass, true);
                    }
                    finally { Object.DestroyImmediate(material); }
                    foreach (ShaderMessage m in ShaderUtil.GetShaderMessages(shader))
                        if (m.severity == ShaderCompilerMessageSeverity.Error)
                            errors.Add(path + "(" + m.line + "): " + m.message);
                }
                foreach (string guid in AssetDatabase.FindAssets("t:ComputeShader", Roots))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                    if (compute == null) { errors.Add(path + ": did not import"); continue; }
                    foreach (ShaderMessage m in ShaderUtil.GetComputeShaderMessages(compute))
                        if (m.severity == ShaderCompilerMessageSeverity.Error)
                            errors.Add(path + "(" + m.line + "): " + m.message);
                }
            }
            finally { LogAssert.ignoreFailingMessages = ignored; }
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
#endif
