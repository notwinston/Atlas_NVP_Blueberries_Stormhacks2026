namespace Atlas.World
{
    /// <summary>
    /// Category of a scanned object: AR Foundation's bounding-box classifications in bit order. The values
    /// go on the wire and into the geometry hash, so they never change.
    /// </summary>
    public enum ObjectCategory : byte
    {
        Unknown = 0, Couch = 1, Table = 2, Bed = 3, Lamp = 4, Plant = 5, Screen = 6, Storage = 7, Bathtub = 8,
        Chair = 9, Dishwasher = 10, Fireplace = 11, Oven = 12, Refrigerator = 13, Sink = 14, Stairs = 15,
        Stove = 16, Toilet = 17, WasherDryer = 18, Keyboard = 19, Mouse = 20, Laptop = 21, Other = 22
    }

    public static class ObjectCategories
    {
        public const int Count = 23;

        static readonly string[] s_names =
        {
            "unknown", "couch", "table", "bed", "lamp", "plant", "screen", "storage", "bathtub",
            "chair", "dishwasher", "fireplace", "oven", "refrigerator", "sink", "stairs", "stove",
            "toilet", "washerDryer", "keyboard", "mouse", "laptop", "other"
        };

        /// <summary>JSON name: "unknown", "couch", ... "washerDryer", ... "other". An undefined value gives "unknown".</summary>
        public static string Name(ObjectCategory category)
        {
            int i = (int)category;
            return s_names[i < Count ? i : 0];
        }

        public static bool TryParse(string name, out ObjectCategory category)
        {
            for (int i = 0; i < Count; i++)
            {
                if (string.Equals(s_names[i], name, System.StringComparison.Ordinal))
                {
                    category = (ObjectCategory)i;
                    return true;
                }
            }
            category = ObjectCategory.Unknown;
            return false;
        }

        /// <summary>The tint index of the category: its own value, 0..22 (palette cell 32 + index).</summary>
        public static int TintIndex(ObjectCategory category)
        {
            int i = (int)category;
            return i < Count ? i : 0;
        }

        /// <summary>From the bits of AR Foundation's BoundingBoxClassifications, passed as uint so that this
        /// assembly needs no AR type: 0 gives Unknown; the lowest set bit k in 0..20 gives the value k + 1;
        /// bit 31 alone gives Other.</summary>
        public static ObjectCategory FromBoundingBoxBits(uint bits)
        {
            if (bits == 0u) return ObjectCategory.Unknown;
            for (int k = 0; k <= 20; k++)
                if ((bits & (1u << k)) != 0u) return (ObjectCategory)(k + 1);
            return ObjectCategory.Other;
        }
    }
}
