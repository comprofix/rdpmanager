namespace RDPManager.Core.Model;

/// <summary>
/// The order of nodes within a folder (owner decision, 2026-11-05): folders first, then connections,
/// each alphabetically. Names compare case-insensitively, with runs of digits compared by value
/// ("server2" before "server10"), as in Windows Explorer.
/// </summary>
public sealed class NodeOrder : IComparer<TreeNode>
{
    public static NodeOrder Instance { get; } = new();

    public int Compare(TreeNode? x, TreeNode? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var byType = (x is FolderNode ? 0 : 1).CompareTo(y is FolderNode ? 0 : 1);
        if (byType != 0)
        {
            return byType;
        }

        var byName = CompareNames(x.Name, y.Name);
        return byName != 0 ? byName : string.CompareOrdinal(x.Id, y.Id);
    }

    /// <summary>Natural, case-insensitive name comparison.</summary>
    public static int CompareNames(string? a, string? b)
    {
        a ??= string.Empty;
        b ??= string.Empty;
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                var startA = i;
                var startB = j;
                while (i < a.Length && char.IsAsciiDigit(a[i]))
                {
                    i++;
                }

                while (j < b.Length && char.IsAsciiDigit(b[j]))
                {
                    j++;
                }

                var numberA = a.AsSpan(startA, i - startA).TrimStart('0');
                var numberB = b.AsSpan(startB, j - startB).TrimStart('0');
                var byValue = numberA.Length != numberB.Length
                    ? numberA.Length.CompareTo(numberB.Length)
                    : numberA.SequenceCompareTo(numberB);
                if (byValue != 0)
                {
                    return byValue;
                }

                continue;
            }

            var byChar = string.Compare(a, i, b, j, 1, StringComparison.CurrentCultureIgnoreCase);
            if (byChar != 0)
            {
                return byChar;
            }

            i++;
            j++;
        }

        var byLength = (a.Length - i).CompareTo(b.Length - j);
        return byLength != 0 ? byLength : string.Compare(a, b, StringComparison.CurrentCulture);
    }
}
