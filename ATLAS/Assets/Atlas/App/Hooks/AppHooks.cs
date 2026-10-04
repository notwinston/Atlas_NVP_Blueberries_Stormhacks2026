using System;

namespace Atlas.App
{
    /// <summary>
    /// Onboarding as the composition root sees it (App/Onboarding, lane C): permissions, pairing, the scan,
    /// the outline confirmation and the headset prompt. While none is installed the rehearsal flow stands in.
    /// </summary>
    public interface IOnboardingFlow
    {
        /// <summary>True when this flow drives the scan and calls the room hand-over itself. False leaves
        /// both to the app's rehearsal flow.</summary>
        bool OwnsFlow { get; }
        /// <summary>Row 19 of the creation order: create objects under app.Boot.Systems, add frame hooks.</summary>
        void Install(AtlasApp app);
        /// <summary>After AtlasServices.Booted: the first screen may show.</summary>
        void Begin();
        void Shutdown();
    }

    /// <summary>The on-device menu (App/Menu, lane D): spaces and parameters in the headset.</summary>
    public interface IAppMenu
    {
        /// <summary>Row 19 of the creation order.</summary>
        void Install(AtlasApp app);
        void Shutdown();
    }

    /// <summary>The scripted performance tour (App/PerfTour): walks the spaces and records the budgets.</summary>
    public interface IPerfTour
    {
        /// <summary>Row 19 of the creation order.</summary>
        void Install(AtlasApp app);
        /// <summary>True while a tour drives the spaces; the rehearsal flow then keeps its hands off.</summary>
        bool Running { get; }
        void Shutdown();
    }

    /// <summary>No onboarding: the rehearsal flow owns the scan and the hand-over.</summary>
    public sealed class NullOnboarding : IOnboardingFlow
    {
        public bool OwnsFlow { get { return false; } }
        public void Install(AtlasApp app) { }
        public void Begin() { }
        public void Shutdown() { }
    }

    /// <summary>No on-device menu.</summary>
    public sealed class NullMenu : IAppMenu
    {
        public void Install(AtlasApp app) { }
        public void Shutdown() { }
    }

    /// <summary>No performance tour.</summary>
    public sealed class NullPerfTour : IPerfTour
    {
        public void Install(AtlasApp app) { }
        public bool Running { get { return false; } }
        public void Shutdown() { }
    }

    /// <summary>
    /// Where the three folders written after the composition root announce themselves, in the way a space
    /// does with SpaceRegistry: one static method marked RuntimeInitializeOnLoadMethod(BeforeSceneLoad) sets
    /// its factory here, and AtlasBootstrap (AfterSceneLoad) runs it at row 19. An empty slot gives the null
    /// implementation, so the app runs with any subset of them.
    /// </summary>
    public static class AppHooks
    {
        public static Func<IOnboardingFlow> Onboarding;
        public static Func<IAppMenu> Menu;
        public static Func<IPerfTour> PerfTour;

        public static IOnboardingFlow CreateOnboarding()
        {
            return (Onboarding != null ? Onboarding() : null) ?? new NullOnboarding();
        }

        public static IAppMenu CreateMenu()
        {
            return (Menu != null ? Menu() : null) ?? new NullMenu();
        }

        public static IPerfTour CreatePerfTour()
        {
            return (PerfTour != null ? PerfTour() : null) ?? new NullPerfTour();
        }

        /// <summary>For tests.</summary>
        public static void Clear()
        {
            Onboarding = null;
            Menu = null;
            PerfTour = null;
        }
    }
}
