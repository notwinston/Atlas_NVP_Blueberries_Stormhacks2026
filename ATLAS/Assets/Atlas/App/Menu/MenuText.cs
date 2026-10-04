using System.Text;
using Atlas.Link;
using TelemetryCsv = Atlas.Telemetry.TelemetryCsv;
using Atlas.World;

namespace Atlas.App.Menu
{
    /// <summary>
    /// The text of the menu, written into the caller's builders (the approach of the HUD, lane D contract
    /// section 5.4): a title line and two columns of <see cref="MenuModel.VisibleRows"/> lines each, names on
    /// the left and values on the right. Engine-free; the view calls it only when the model's revision moved.
    /// </summary>
    public static class MenuText
    {
        /// <summary>Longest name and value shown; a longer one is cut so the columns never run into each other.</summary>
        public const int MaxNameChars = 26, MaxValueChars = 22;

        static readonly string[] s_tiers = { "nominal", "fair", "serious", "critical", "high" };

        /// <summary>"ATLAS  Quality   3-10 of 23".</summary>
        public static void BuildTitle(StringBuilder sb, MenuModel model)
        {
            sb.Length = 0;
            sb.Append("ATLAS  ").Append(MenuModel.PageName(model.Page));
            int count = model.RowCount;
            if (count <= MenuModel.VisibleRows) return;
            sb.Append("   ");
            TelemetryCsv.AppendInt(sb, model.Top + 1);
            sb.Append('-');
            int last = model.Top + MenuModel.VisibleRows;
            TelemetryCsv.AppendInt(sb, last < count ? last : count);
            sb.Append(" of ");
            TelemetryCsv.AppendInt(sb, count);
        }

        /// <summary>The left column: one line per visible row, empty lines below the last row.</summary>
        public static void BuildNames(StringBuilder sb, MenuModel model)
        {
            sb.Length = 0;
            for (int slot = 0; slot < MenuModel.VisibleRows; slot++)
            {
                if (slot > 0) sb.Append('\n');
                int index = model.Top + slot;
                if (index >= model.RowCount) continue;
                int start = sb.Length;
                AppendName(sb, model, index);
                Cut(sb, start, MaxNameChars);
            }
        }

        /// <summary>The right column, line for line with <see cref="BuildNames"/>.</summary>
        public static void BuildValues(StringBuilder sb, MenuModel model)
        {
            sb.Length = 0;
            for (int slot = 0; slot < MenuModel.VisibleRows; slot++)
            {
                if (slot > 0) sb.Append('\n');
                int index = model.Top + slot;
                if (index >= model.RowCount) continue;
                int start = sb.Length;
                AppendValue(sb, model, index);
                Cut(sb, start, MaxValueChars);
            }
        }

        static void AppendName(StringBuilder sb, MenuModel model, int index)
        {
            MenuRow row = model.Row(index);
            switch (row.Kind)
            {
                case MenuRowKind.Page:
                    sb.Append("Page");
                    break;
                case MenuRowKind.Heading:
                    sb.Append("- ");
                    AppendGroup(sb, row.Label);
                    if (!model.Registry.IsGroupActive(row.Label)) sb.Append(" (inactive)");
                    break;
                case MenuRowKind.Param:
                    sb.Append(row.Entry.Name);
                    // The mark of a value that is not the default.
                    if (row.Entry.Value != row.Entry.Default) sb.Append(" *");
                    break;
                case MenuRowKind.Space:
                    sb.Append(SpaceIds.Code(row.Space)).Append(' ').Append(SpaceIds.DisplayName(row.Space));
                    break;
                default:
                    sb.Append(row.Label);
                    break;
            }
        }

        static void AppendValue(StringBuilder sb, MenuModel model, int index)
        {
            MenuRow row = model.Row(index);
            switch (row.Kind)
            {
                case MenuRowKind.Page:
                    sb.Append(MenuModel.PageName(model.Page)).Append("  ");
                    TelemetryCsv.AppendInt(sb, (int)model.Page + 1);
                    sb.Append('/');
                    TelemetryCsv.AppendInt(sb, MenuModel.PageCount);
                    break;
                case MenuRowKind.Param:
                    if (index == model.Selected && model.HasPending)
                    {
                        // A value that waits for Confirm, because applying it rebuilds geometry.
                        AppendParamValue(sb, row.Entry, model.PendingValue);
                        sb.Append(" (apply)");
                    }
                    else
                    {
                        AppendParamValue(sb, row.Entry, row.Entry.Value);
                    }
                    break;
                case MenuRowKind.Space:
                    sb.Append(model.IsCurrent(row.Space) ? "here" : "go");
                    break;
                case MenuRowKind.Command:
                    sb.Append("go");
                    break;
                case MenuRowKind.Info:
                    AppendInfo(sb, row.Info, in model.Status);
                    break;
            }
        }

        /// <summary>A parameter's value as the menu shows it: "on", an enum's name, a whole number, or a float
        /// with as many decimals as one step needs.</summary>
        public static void AppendParamValue(StringBuilder sb, ParamEntry entry, float value)
        {
            switch (entry.Kind)
            {
                case ParamKind.Bool:
                    sb.Append(value >= 0.5f ? "on" : "off");
                    break;
                case ParamKind.Enum:
                {
                    string name = entry.EnumName((int)value);
                    if (name != null) sb.Append(name);
                    else TelemetryCsv.AppendInt(sb, (int)value);
                    break;
                }
                case ParamKind.Int:
                    TelemetryCsv.AppendInt(sb, (int)value);
                    break;
                default:
                    TelemetryCsv.AppendFixed(sb, value, Decimals(MenuModel.StepSize(entry)));
                    break;
            }
        }

        /// <summary>Digits after the point that show one step of this size.</summary>
        public static int Decimals(float step)
        {
            if (step >= 1f) return 1;
            if (step >= 0.1f) return 2;
            if (step >= 0.01f) return 3;
            return 4;
        }

        static void AppendInfo(StringBuilder sb, MenuInfo info, in MenuStatus status)
        {
            switch (info)
            {
                case MenuInfo.Tier:
                    sb.Append(status.Tier >= 0 && status.Tier < s_tiers.Length ? s_tiers[status.Tier] : "?");
                    sb.Append("  therm ");
                    TelemetryCsv.AppendInt(sb, status.Thermal);
                    break;
                case MenuInfo.Link:
                    sb.Append(status.Link ?? "offline");
                    if (status.Link != null && status.Link != "offline")
                    {
                        sb.Append("  ");
                        TelemetryCsv.AppendInt(sb, status.RttMs);
                        sb.Append(" ms");
                    }
                    break;
                case MenuInfo.Station:
                    if (string.IsNullOrEmpty(status.StationHost)) sb.Append("not paired");
                    else
                    {
                        sb.Append(status.StationHost).Append(':');
                        TelemetryCsv.AppendInt(sb, status.StationPort);
                    }
                    break;
                case MenuInfo.Session:
                    sb.Append(string.IsNullOrEmpty(status.Session) ? "-" : status.Session);
                    break;
                case MenuInfo.Version:
                    sb.Append(string.IsNullOrEmpty(status.Version) ? "-" : status.Version);
                    break;
                case MenuInfo.Room:
                    sb.Append(string.IsNullOrEmpty(status.Room) ? "none" : status.Room);
                    break;
            }
        }

        // A space's group is shown the way the Console shows it: "S1 Glued".
        static void AppendGroup(StringBuilder sb, string group)
        {
            for (int i = 0; i < SpaceIds.Count; i++)
            {
                var id = (SpaceId)i;
                if (SpaceIds.ParamGroup(id) != group) continue;
                sb.Append(SpaceIds.Code(id)).Append(' ').Append(SpaceIds.DisplayName(id));
                return;
            }
            sb.Append(group);
        }

        static void Cut(StringBuilder sb, int start, int max)
        {
            if (sb.Length - start > max) sb.Length = start + max;
        }
    }
}
