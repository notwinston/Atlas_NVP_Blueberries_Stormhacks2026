namespace Atlas.Spaces.Home
{
    /// <summary>Announces Home to SpaceLab before the first scene loads (lane A contract, section 5.1).</summary>
    static class HomeRegistration
    {
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void Register() =>
            Atlas.World.SpaceRegistry.Register(Atlas.World.SpaceId.Home, HomeSpace.Create, HomeSpace.DeclareParams);
    }
}
