namespace Atlas.Blades.Lung.Sky.Tests
{
    public sealed class FakePresenter : INeedleSkyPresenter
    {
        public bool Visible; public int SetVisibleCalls, PresentCalls; public NeedleSkyFrame Last;
        public void SetVisible(bool visible) { Visible = visible; SetVisibleCalls++; }
        public void Present(in NeedleSkyFrame frame) { Last = frame; PresentCalls++; }
    }
}
