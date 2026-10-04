using System;
using Atlas.Input;
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.World;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Hands
{
    /// <summary>
    /// The drawn hands (documents/ATLAS_Hands_Design.md section 5): two dynamic meshes skinned on the CPU every
    /// frame in step 6, each on the world layer with its own pooled Atlas/Surface material (no warp, no fog,
    /// two-sided, opacity by the shader's dither), and one small ring per hand for touch and contact marks.
    /// The meshes are in world coordinates, so their objects never move and must never sit under room content.
    /// </summary>
    public sealed class HandsView : IDisposable
    {
        sealed class Hand
        {
            public HandsMeshTemplate Template;
            public GameObject Object;
            public MeshRenderer Renderer;
            public Mesh Mesh;
            public Material Material;
            public NativeArray<AtlasVertex> Vertices;
            public NativeArray<int> Indices;
            public bool WasEnabled;
        }

        sealed class Mark
        {
            public GameObject Object;
            public MeshRenderer Renderer;
            public Mesh Mesh;
            public Material Material;
            public float Opacity;       // what is drawn now
            public float Flash;         // seconds left of the press flash
            public bool WasEnabled;
        }

        const int RingSegments = 24;
        const float RingInner = 0.36f;      // of a unit diameter
        const float PressFlashSeconds = 0.12f;
        const float HoverFarMetres = 0.04f, HoverNearMetres = 0.016f;

        readonly HandsRuntime _runtime;
        readonly MaterialPool _pool;
        readonly Hand[] _hands = new Hand[2];
        readonly Mark[] _marks = new Mark[2];
        readonly float2 _paletteUv = AtlasPalette.Uv(AtlasPalette.AvatarBody);
        GameObject _root;
        bool _disposed, _stillHidden;

        HandsView(HandsRuntime runtime, MaterialPool pool)
        {
            _runtime = runtime;
            _pool = pool;
        }

        /// <summary>Builds the objects under parent (never under room content). Nothing is drawn until a hand is tracked.</summary>
        public static HandsView Create(Transform parent, MaterialPool pool, HandsRuntime runtime)
        {
            if (pool == null) throw new ArgumentNullException("pool");
            if (runtime == null) throw new ArgumentNullException("runtime");
            var view = new HandsView(runtime, pool);
            view._root = new GameObject("Atlas Hands");
            view._root.transform.SetParent(parent, false);
            view._hands[0] = view.NewHand(HandSide.Left, "Hands Left");
            view._hands[1] = view.NewHand(HandSide.Right, "Hands Right");
            view._marks[0] = view.NewMark("Hands Mark Left");
            view._marks[1] = view.NewMark("Hands Mark Right");
            return view;
        }

        Hand NewHand(HandSide side, string name)
        {
            var hand = new Hand();
            hand.Template = HandsMeshTemplate.Build(side);
            hand.Vertices = new NativeArray<AtlasVertex>(hand.Template.VertexCount, Allocator.Persistent);
            hand.Indices = new NativeArray<int>(hand.Template.IndexCount, Allocator.Persistent);
            hand.Template.CopyIndices(hand.Indices);
            hand.Mesh = AtlasMesh.CreateDynamic(name, hand.Template.VertexCount, hand.Template.IndexCount);
            hand.Material = _pool.Rent();
            // Never fogged, kept through a space switch, dissolved close to an eye, drawn from both sides so a
            // hand labelled as the wrong side never shows a hole (section 5.3).
            MaterialPool.SetSurface(hand.Material, 1f, 0f, HandsConstants.NearFadeMetres, 0f);
            MaterialPool.SetCull(hand.Material, CullMode.Off);
            MaterialPool.SetTint(hand.Material, new float3(1f, 1f, 1f), 0f);
            hand.Object = NewObject(name, hand.Mesh, hand.Material, out hand.Renderer);
            return hand;
        }

        Mark NewMark(string name)
        {
            var mark = new Mark();
            mark.Mesh = BuildRing(name);
            mark.Material = _pool.Rent();
            MaterialPool.SetSurface(mark.Material, 1f, 0f, 0f, 0f);
            MaterialPool.SetCull(mark.Material, CullMode.Off);
            MaterialPool.SetTint(mark.Material, new float3(1f, 1f, 1f), 0f);
            mark.Object = NewObject(name, mark.Mesh, mark.Material, out mark.Renderer);
            return mark;
        }

        GameObject NewObject(string name, Mesh mesh, Material material, out MeshRenderer renderer)
        {
            var go = new GameObject(name) { layer = 0 };
            go.transform.SetParent(_root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.enabled = false;
            return go;
        }

        // A flat ring of unit diameter in the local XY plane, in an emissive palette cell.
        static Mesh BuildRing(string name)
        {
            int vertexCount = RingSegments * 2, indexCount = RingSegments * 6;
            var vertices = new NativeArray<AtlasVertex>(vertexCount, Allocator.Temp);
            var indices = new NativeArray<int>(indexCount, Allocator.Temp);
            float2 uv = AtlasPalette.Uv(AtlasPalette.Reticle);
            uint colour = AtlasVertex.PackColor(255, 0);
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * (2f * math.PI / RingSegments);
                float3 direction = new float3(math.cos(angle), math.sin(angle), 0f);
                AtlasVertex v;
                v.Normal = new float3(0f, 0f, 1f);
                v.Color = colour;
                v.Uv0 = uv;
                v.Uv1 = float2.zero;
                v.Position = direction * 0.5f;
                vertices[i * 2] = v;
                v.Position = direction * (0.5f * RingInner);
                vertices[i * 2 + 1] = v;
                int next = (i + 1) % RingSegments;
                int k = i * 6;
                indices[k] = i * 2; indices[k + 1] = next * 2; indices[k + 2] = i * 2 + 1;
                indices[k + 3] = i * 2 + 1; indices[k + 4] = next * 2; indices[k + 5] = next * 2 + 1;
            }
            Mesh mesh = AtlasMesh.CreateDynamic(name, vertexCount, indexCount);
            AtlasMesh.Upload(mesh, vertices, vertexCount, indices, indexCount, new Bounds(Vector3.zero, new Vector3(1f, 1f, 0.01f)));
            vertices.Dispose();
            indices.Dispose();
            return mesh;
        }

        /// <summary>Step 6: skins the hands that are visible and places the marks. No allocation.</summary>
        public void Upload(in FrameContext frame)
        {
            if (_disposed) return;
            bool hidden = _runtime.Hidden;
            UploadHand(_hands[0], HandSide.Left, hidden);
            UploadHand(_hands[1], HandSide.Right, hidden);
            HandsTouchState touch = _runtime.InputTouch;
            UploadMark(_marks[0], frame.DeltaTime, hidden, in touch.Left, in _runtime.Touch(HandSide.Left));
            UploadMark(_marks[1], frame.DeltaTime, hidden, in touch.Right, in _runtime.Touch(HandSide.Right));
        }

        void UploadHand(Hand hand, HandSide side, bool hidden)
        {
            ref readonly HandPose pose = ref _runtime.Pose(side);
            if (hidden || !pose.Tracked || pose.Opacity <= 0.001f)
            {
                if (hand.Renderer.enabled) hand.Renderer.enabled = false;
                return;
            }
            ref readonly TrackedHand tracked = ref _runtime.Tracked(side);
            float3 boundsMin, boundsMax;
            HandsSkin.Skin(hand.Template, in tracked.World, tracked.PalmNormal, _paletteUv, AtlasShading.LightDirection,
                hand.Vertices, out boundsMin, out boundsMax);
            var bounds = new Bounds((Vector3)(0.5f * (boundsMin + boundsMax)), (Vector3)(boundsMax - boundsMin));
            AtlasMesh.Upload(hand.Mesh, hand.Vertices, hand.Template.VertexCount, hand.Indices, hand.Template.IndexCount, bounds);
            MaterialPool.SetTint(hand.Material, new float3(1f, 1f, 1f), pose.Opacity);
            if (!hand.Renderer.enabled) hand.Renderer.enabled = true;
        }

        // One ring per hand: on a target's face while the fingertip hovers or presses it (section 6.2), else on
        // a surface of the world the fingertip is near or on (section 6.5).
        void UploadMark(Mark mark, float deltaTime, bool hidden, in HandsTouchHand target, in WorldTouch world)
        {
            float wanted = 0f, diameter = HandsConstants.RingMetres;
            float3 point = float3.zero, normal = new float3(0f, 0f, 1f);
            if (target.Fired) mark.Flash = PressFlashSeconds;
            if (!hidden && target.Target.IsValid && (target.Hover > 0f || target.Pressed || mark.Flash > 0f))
            {
                point = target.FacePoint;
                normal = target.FaceNormal;
                bool pressed = target.Pressed || mark.Flash > 0f;
                wanted = pressed ? 1f : math.lerp(0.35f, 0.9f, math.saturate(target.Hover));
                diameter = pressed ? HoverFarMetres : math.lerp(HoverFarMetres, HoverNearMetres, math.saturate(target.Hover));
            }
            else if (!hidden && world.Near)
            {
                point = world.Point;
                normal = world.Normal;
                float closeness = 1f - math.saturate(world.Distance / HandsConstants.GlowMetres);
                wanted = world.Contact ? 1f : math.lerp(0.25f, 0.8f, closeness);
                diameter = world.Contact ? HandsConstants.RingMetres
                                         : math.lerp(HandsConstants.GlowFarMetres, HandsConstants.GlowNearMetres, closeness);
            }
            mark.Flash = math.max(0f, mark.Flash - deltaTime);

            if (wanted > 0f)
            {
                mark.Opacity = wanted;
                Transform t = mark.Object.transform;
                float3 forward = math.normalizesafe(normal, new float3(0f, 0f, 1f));
                t.SetPositionAndRotation((Vector3)(point + forward * HandsConstants.MarkLiftMetres),
                    Quaternion.LookRotation((Vector3)forward, Mathf.Abs(forward.y) > 0.99f ? Vector3.forward : Vector3.up));
                t.localScale = new Vector3(diameter, diameter, 1f);
            }
            else
            {
                // The ring left behind fades where it was (section 6.5).
                mark.Opacity = math.max(0f, mark.Opacity - deltaTime / HandsConstants.MarkFadeSeconds);
            }

            if (mark.Opacity <= 0.001f)
            {
                if (mark.Renderer.enabled) mark.Renderer.enabled = false;
                return;
            }
            MaterialPool.SetTint(mark.Material, new float3(1f, 1f, 1f), mark.Opacity);
            if (!mark.Renderer.enabled) mark.Renderer.enabled = true;
        }

        /// <summary>
        /// True hides both hands and both marks from every camera until false, for the hexagon transition's
        /// stills (section 7.4: the hands stay live through a space switch, so a still must not hold them). Each
        /// renderer's own enabled state is kept and put back, so the per-frame upload is not disturbed.
        /// </summary>
        public void SetHiddenForStill(bool hidden)
        {
            if (_disposed) return;
            if (hidden == _stillHidden) return;
            _stillHidden = hidden;
            for (int i = 0; i < 2; i++)
            {
                Hide(_hands[i].Renderer, ref _hands[i].WasEnabled, hidden);
                Hide(_marks[i].Renderer, ref _marks[i].WasEnabled, hidden);
            }
        }

        static void Hide(MeshRenderer renderer, ref bool was, bool hidden)
        {
            if (renderer == null) return;
            if (hidden)
            {
                was = renderer.enabled;
                renderer.enabled = false;
            }
            else
            {
                renderer.enabled = was;
            }
        }

        /// <summary>Returns the materials to the pool and frees the native buffers. Safe to call twice.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = 0; i < 2; i++)
            {
                Hand hand = _hands[i];
                if (hand != null)
                {
                    if (hand.Vertices.IsCreated) hand.Vertices.Dispose();
                    if (hand.Indices.IsCreated) hand.Indices.Dispose();
                    if (hand.Material != null) _pool.Return(hand.Material);
                    Kill(hand.Mesh);
                }
                Mark mark = _marks[i];
                if (mark != null)
                {
                    if (mark.Material != null) _pool.Return(mark.Material);
                    Kill(mark.Mesh);
                }
            }
            Kill(_root);
            _root = null;
        }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
