namespace Atlas.Spaces.Glued
{
    /// <summary>Announces S1 to SpaceLab before the composition root runs (lane A contract, section 5.1).</summary>
    static class GluedRegistration
    {
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            Atlas.World.SpaceRegistry.Register(Atlas.World.SpaceId.Glued, GluedSpace.Create, GluedSpace.DeclareParams);
        }
    }
}
