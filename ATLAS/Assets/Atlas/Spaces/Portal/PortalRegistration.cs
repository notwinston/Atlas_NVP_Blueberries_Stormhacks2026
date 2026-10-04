namespace Atlas.Spaces.Portal
{
    /// <summary>Announces Impossible Rooms to SpaceLab before the first scene loads (lane A contract, section 5.1).</summary>
    static class PortalRegistration
    {
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void Register() =>
            Atlas.World.SpaceRegistry.Register(Atlas.World.SpaceId.Portal, PortalSpace.Create, PortalSpace.DeclareParams);
    }
}
