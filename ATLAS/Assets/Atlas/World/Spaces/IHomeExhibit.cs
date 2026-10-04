namespace Atlas.World
{
    /// <summary>
    /// A use case shown inside Home (not a space of its own). Published with <c>AtlasServices.Register&lt;IHomeExhibit&gt;</c>.
    /// The exhibit menu of the Playground reaches the network blade through this seam (contract network.md section 6.6).
    /// </summary>
    public interface IHomeExhibit
    {
        /// <summary>The exhibit menu row text: "Network".</summary>
        string Label { get; }
        bool Ready { get; }
        bool Active { get; }
        /// <summary>Shows the exhibit in Home; false when it could not.</summary>
        bool Begin();
        /// <summary>Hides it; Home and the RoomModel are left as they were.</summary>
        void End();
    }
}
