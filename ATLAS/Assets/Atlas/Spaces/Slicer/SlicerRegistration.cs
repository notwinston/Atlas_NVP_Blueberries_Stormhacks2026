namespace Atlas.Spaces.Slicer
{
    /// <summary>Announces the Slicer to SpaceLab before the first scene loads (lane A contract, section 5.1).</summary>
    static class SlicerRegistration
    {
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void Register() =>
            Atlas.World.SpaceRegistry.Register(Atlas.World.SpaceId.Slicer, SlicerSpace.Create, SlicerSpace.DeclareParams);
    }
}
