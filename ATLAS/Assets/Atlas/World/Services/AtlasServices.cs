using System;
using System.Collections.Generic;

namespace Atlas.World
{
    /// <summary>
    /// Typed service locator. The composition root fills it; nothing searches the scene (contract README F3).
    /// The type argument is the key: publish under the interface, not the class. Main thread only.
    /// </summary>
    public static class AtlasServices
    {
        // One static slot per key type: Get is a field read, with no dictionary and no allocation.
        static class Slot<T> where T : class
        {
            public static T Value;
            public static bool Listed;
            public static readonly Action Reset = () => Value = null;
        }

        static readonly List<Action> s_resets = new List<Action>(32);
        static Action s_booted;
        static bool s_bootCompleted;

        /// <summary>Publishes a service under the type T. Replaces an earlier one. Null: ArgumentNullException.</summary>
        public static void Register<T>(T service) where T : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service), "No service to publish as " + typeof(T).Name);
            if (!Slot<T>.Listed)
            {
                Slot<T>.Listed = true;
                s_resets.Add(Slot<T>.Reset);
            }
            Slot<T>.Value = service;
        }

        /// <summary>Publishes a service under the type T and returns the one it replaced, or null.</summary>
        public static T Replace<T>(T service) where T : class
        {
            T previous = Slot<T>.Value;
            Register(service);
            return previous;
        }

        /// <summary>Removes the service if it is the one published under T.</summary>
        public static void Unregister<T>(T service) where T : class
        {
            if (service != null && ReferenceEquals(Slot<T>.Value, service)) Slot<T>.Value = null;
        }

        /// <summary>The service, or InvalidOperationException naming T when none is published.</summary>
        public static T Get<T>() where T : class
        {
            T value = Slot<T>.Value;
            if (value == null) throw new InvalidOperationException("No service is published as " + typeof(T).FullName);
            return value;
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            service = Slot<T>.Value;
            return service != null;
        }

        /// <summary>True when a service is published under T.</summary>
        public static bool Has<T>() where T : class
        {
            return Slot<T>.Value != null;
        }

        /// <summary>True from the end of the composition root until Clear.</summary>
        public static bool BootCompleted { get { return s_bootCompleted; } }

        /// <summary>Raised once when the composition root has finished. A late subscriber is called at once.</summary>
        public static event Action Booted
        {
            add
            {
                if (value == null) return;
                if (s_bootCompleted) value();
                else s_booted += value;
            }
            remove { s_booted -= value; }
        }

        /// <summary>
        /// Called by the composition root as its last step: sets BootCompleted and raises Booted once.
        /// A second call before Clear does nothing.
        /// </summary>
        public static void MarkBooted()
        {
            if (s_bootCompleted) return;
            s_bootCompleted = true;
            Action handlers = s_booted;
            s_booted = null;
            if (handlers != null) handlers();
        }

        /// <summary>Removes everything: services, Booted subscribers and the boot flag. Called first by the composition root and by tests.</summary>
        public static void Clear()
        {
            for (int i = 0; i < s_resets.Count; i++) s_resets[i]();
            s_booted = null;
            s_bootCompleted = false;
        }
    }
}
