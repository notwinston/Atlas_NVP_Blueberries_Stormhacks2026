using System;
using Atlas.Link;

namespace Atlas.World
{
    /// <summary>
    /// Where each space assembly announces itself, so that Atlas.App needs no reference to it
    /// (contract README F3). Main thread only.
    /// </summary>
    public static class SpaceRegistry
    {
        static readonly Func<ISpaceModule>[] s_factories = new Func<ISpaceModule>[SpaceIds.Count];
        static readonly Action<ParamRegistry>[] s_declare = new Action<ParamRegistry>[SpaceIds.Count];

        /// <summary>Registers or replaces the entry of a space. Safe to call twice (no domain reload in the Editor).</summary>
        /// <param name="id">One of the seven spaces.</param>
        /// <param name="factory">Creates a new GameObject named like the scene, adds the module, returns it.</param>
        /// <param name="declareParams">Declares the space's parameters; may be null.</param>
        public static void Register(SpaceId id, Func<ISpaceModule> factory, Action<ParamRegistry> declareParams = null)
        {
            if (!SpaceIds.IsDefined(id)) throw new ArgumentOutOfRangeException(nameof(id));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            s_factories[(int)id] = factory;
            s_declare[(int)id] = declareParams;
        }

        public static bool IsRegistered(SpaceId id)
        {
            return SpaceIds.IsDefined(id) && s_factories[(int)id] != null;
        }

        /// <summary>Number of registered spaces.</summary>
        public static int RegisteredCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < s_factories.Length; i++) if (s_factories[i] != null) n++;
                return n;
            }
        }

        /// <summary>Runs the factory. Null when the space is not registered.</summary>
        public static ISpaceModule Create(SpaceId id)
        {
            return IsRegistered(id) ? s_factories[(int)id]() : null;
        }

        /// <summary>Runs every declareParams delegate, in SpaceId order. Called once by SpaceLab at boot.</summary>
        public static void DeclareAllParams(ParamRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            for (int i = 0; i < s_declare.Length; i++)
                if (s_factories[i] != null && s_declare[i] != null) s_declare[i](registry);
        }

        /// <summary>For tests.</summary>
        public static void Clear()
        {
            Array.Clear(s_factories, 0, s_factories.Length);
            Array.Clear(s_declare, 0, s_declare.Length);
        }
    }
}
