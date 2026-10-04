using Atlas.Spaces.SmallPlanet;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Atlas.Spaces.CurvedWalk.Tests
{
    // P1-B09: creates and saves Space_CurvedWalk.unity and Space_SmallPlanet.unity (the scenes lane A's Atlas_Boot loads).
    // Explicit: run once with the runner's filter argument "Atlas.Spaces.CurvedWalk.Tests.SpaceSceneBuilder.Build"; the saved assets are committed.
    public class SpaceSceneBuilder
    {
        const string CurvedPath = "Assets/Atlas/Spaces/CurvedWalk/Scenes/Space_CurvedWalk.unity";
        const string PlanetPath = "Assets/Atlas/Spaces/SmallPlanet/Scenes/Space_SmallPlanet.unity";

        [Test, Explicit("creates and saves the two space scenes")]
        public void Build()
        {
            var shader = Shader.Find("Atlas/AtlasSurface");
            Assert.IsNotNull(shader);
            System.IO.Directory.CreateDirectory("Assets/Atlas/Spaces/SmallPlanet/Scenes");
            // one material per scene so the shader is referenced by the scene (it is then included in a build)
            var matC = MakeMaterial("Assets/Atlas/Spaces/CurvedWalk/Scenes/S2_Surface.mat", shader);
            var matP = MakeMaterial("Assets/Atlas/Spaces/SmallPlanet/Scenes/S4_Surface.mat", shader);

            var sc = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Space_CurvedWalk");
            go.AddComponent<CurvedWalkSpaceScene>().sharedMaterial = matC;
            Assert.IsTrue(EditorSceneManager.SaveScene(sc, CurvedPath));

            sc = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            go = new GameObject("Space_SmallPlanet");
            go.AddComponent<SmallPlanetSpaceScene>().sharedMaterial = matP;
            Assert.IsTrue(EditorSceneManager.SaveScene(sc, PlanetPath));
            AssetDatabase.SaveAssets();
        }

        static Material MakeMaterial(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
