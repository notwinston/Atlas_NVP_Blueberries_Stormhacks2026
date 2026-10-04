namespace Atlas.Spaces.AlignedCells
{
    /// <summary>Announces S3 to SpaceLab before the composition root runs (lane A contract, section 5.1).</summary>
    static class AlignedCellsRegistration
    {
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void Register()
        {
            Atlas.World.SpaceRegistry.Register(Atlas.World.SpaceId.AlignedCells, AlignedCellsSpace.Create, AlignedCellsSpace.DeclareParams);
        }
    }
}
