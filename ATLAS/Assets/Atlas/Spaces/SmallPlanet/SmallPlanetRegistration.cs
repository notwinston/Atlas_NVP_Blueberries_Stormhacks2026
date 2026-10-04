namespace Atlas.Spaces.SmallPlanet
{
    /// <summary>Announces S4 to SpaceLab before the composition root runs (lane A contract, section 5.1).</summary>
    static class SmallPlanetRegistration
    {
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void Register()
        {
            Atlas.World.SpaceRegistry.Register(Atlas.World.SpaceId.SmallPlanet, SmallPlanetSpace.Create, SmallPlanetSpace.DeclareParams);
        }
    }
}
