namespace Atlas.Spaces.CurvedWalk
{
    /// <summary>Announces S2 to SpaceLab before the composition root runs (lane A contract, section 5.1).</summary>
    static class CurvedWalkRegistration
    {
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void Register()
        {
            Atlas.World.SpaceRegistry.Register(Atlas.World.SpaceId.CurvedWalk, CurvedWalkSpace.Create, CurvedWalkSpace.DeclareParams);
        }
    }
}
