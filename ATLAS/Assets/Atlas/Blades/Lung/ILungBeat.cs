// PLACEHOLDER (lane A, P2-A32): contract surface only; replaced by lane A's implementation
namespace Atlas.Blades.Lung
{
    /// <summary>Master Plan §10.2.2 beats a-h (deviation PH-13).</summary>
    public enum LungBeat { A, B, C, D, E, F, G, H }

    /// <summary>The interface lanes B and D implement; lane A's director enters beats through it (P2-A32).</summary>
    public interface ILungBeat
    {
        LungBeat Beat { get; }
        void Enter();         // from any state: the Console SceneCmd enters any beat
        void Exit();
        bool Completed { get; }   // the director advances when true
    }
}
