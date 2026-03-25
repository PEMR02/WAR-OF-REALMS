using System.Text;

namespace Project.UI
{
    /// <summary>Nombres legibles para HUD a partir de ids técnicos (town_center → Town center).</summary>
    public static class SelectionDisplayName
    {
        public static string HumanizeId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = raw.Trim();
            int p = s.IndexOf('(');
            if (p > 0) s = s.Substring(0, p).Trim();
            s = s.Replace("(Clone)", "", System.StringComparison.OrdinalIgnoreCase).Trim();
            s = s.Replace('_', ' ');
            if (s.Length == 0) return "";
            var sb = new StringBuilder(s.Length);
            bool cap = true;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c))
                {
                    cap = true;
                    sb.Append(c);
                    continue;
                }
                sb.Append(cap ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                cap = false;
            }
            return sb.ToString();
        }
    }
}
