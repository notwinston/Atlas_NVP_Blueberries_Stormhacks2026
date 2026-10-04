using System;
using Atlas.Rendering;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Spaces.SmallPlanet
{
    /// <summary>
    /// The Small Planet's daytime sky (SmallPlanetSkyMesh): the look's background gradient set to a pale horizon and
    /// a blue zenith, the fog set to that horizon, and the sun and clouds as two renderers sharing one pooled
    /// Atlas/Surface material (warp mode 0, fog weight 0, both faces). Head-locked in position, world-fixed in
    /// rotation: each frame the sky moves to the head and keeps the world's axes, so it never swims when the head
    /// turns; only the clouds turn, slowly, about the vertical. Built once on Enter, released on Exit.
    /// </summary>
    /// <remarks>
    /// The look's colours need no restoring: SpaceLab resets them between spaces (AtlasLook.ResetForSpace). The eye
    /// cameras clear with that background (StereoRig); their clear colour, used only if the background were
    /// missing, is set to the horizon here and given back on Dispose.
    /// </remarks>
    public sealed class SmallPlanetSky : IDisposable
    {
        readonly MaterialPool pool;
        readonly GameObject root, sun, clouds;
        readonly Mesh sunMesh, cloudMesh;
        readonly Material material;
        readonly Camera[] eyes = new Camera[2];
        readonly Color[] savedClear = new Color[2];

        bool view3d;

        /// <summary>The look's part of the sky, with nothing to draw (the Station, or a build without a look's pool).
        /// view3d (planet.view3d): fog and zenith both the meadow green, the S3 view's "sky" of far ground.</summary>
        public static void ApplyLook(bool view3d = false)
        {
            AtlasLook.SetFogColor(view3d ? SmallPlanetSkyMesh.MeadowLinear : SmallPlanetSkyMesh.HorizonLinear, true);
            AtlasLook.SetSkyTop(view3d ? SmallPlanetSkyMesh.MeadowLinear : SmallPlanetSkyMesh.ZenithLinear);
        }

        public SmallPlanetSky(Transform parent, MaterialPool materialPool, IStereoRig rig, bool view3d = false)
        {
            pool = materialPool ?? throw new ArgumentNullException(nameof(materialPool));
            this.view3d = view3d;
            ApplyLook(view3d);

            var sunData = new AtlasMeshBuilder(64, 192);
            SmallPlanetSkyMesh.BuildSun(sunData);
            sunMesh = AtlasMesh.Create(sunData, "SmallPlanetSun");
            var cloudData = new AtlasMeshBuilder(4096, 4096);
            SmallPlanetSkyMesh.BuildClouds(cloudData);
            cloudMesh = AtlasMesh.Create(cloudData, "SmallPlanetClouds");

            material = pool.Rent();
            MaterialPool.SetWarpMode(material, 0);                 // a plain transform: at infinity, never bent or clipped at 2.6 rad
            MaterialPool.SetSurface(material, 1f, 0f, 0f, 1f);     // not fogged; follows the world fade like the background
            MaterialPool.SetCull(material, CullMode.Off);

            root = new GameObject("SmallPlanetSky") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(parent, false);
            sun = MakeChild("Sun", sunMesh);
            clouds = MakeChild("Clouds", cloudMesh);

            if (rig != null)
                for (int e = 0; e < 2; e++)
                {
                    Camera eye = rig.GetEyeCamera((Eye)e);
                    if (eye == null) continue;
                    eyes[e] = eye;
                    savedClear[e] = eye.backgroundColor;
                }
            ApplyClear();
            sun.SetActive(!view3d);
            clouds.SetActive(!view3d);
        }

        /// <summary>
        /// planet.view3d: on hides the sun and clouds and turns fog, zenith and the eyes' clear colour meadow green
        /// (in S3 the ground fills the sky); off is the daytime sky exactly as built.
        /// </summary>
        public bool View3d
        {
            get => view3d;
            set
            {
                if (view3d == value) return;
                view3d = value;
                ApplyLook(value);
                ApplyClear();
                sun.SetActive(!value);
                clouds.SetActive(!value);
            }
        }

        void ApplyClear()
        {
            float3 h = view3d ? SmallPlanetSkyMesh.MeadowSrgb : SmallPlanetSkyMesh.HorizonSrgb;
            for (int e = 0; e < 2; e++)
                if (eyes[e] != null) eyes[e].backgroundColor = new Color(h.x, h.y, h.z, 1f);
        }

        public MeshRenderer SunRenderer => sun.GetComponent<MeshRenderer>();
        public MeshRenderer CloudRenderer => clouds.GetComponent<MeshRenderer>();
        public Material Material => material;
        public Transform Root => root.transform;

        GameObject MakeChild(string name, Mesh mesh)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(root.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        /// <summary>Step 6: the sky to the head (world axes kept), the clouds turned by the time.</summary>
        public void Upload(float3 headPosition, double timeSeconds)
        {
            if (math.all(math.isfinite(headPosition))) root.transform.SetPositionAndRotation(headPosition, Quaternion.identity);
            float yaw = (float)((timeSeconds * SmallPlanetSkyMesh.CloudDriftDegPerSecond) % 360.0);
            clouds.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        public void Dispose()
        {
            for (int e = 0; e < 2; e++)
            {
                if (eyes[e] != null) eyes[e].backgroundColor = savedClear[e];
                eyes[e] = null;
            }
            Kill(root);
            Kill(sunMesh);
            Kill(cloudMesh);
            if (material != null && pool.Owns(material)) pool.Return(material);
        }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
