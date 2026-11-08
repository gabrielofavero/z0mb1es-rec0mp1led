using System.Text;

namespace Port.Hooks;

public static class DesktopCredits
{
    public static StringBuilder[] AddPortCredit(StringBuilder[] original)
    {
        const int insertionIndex = 4;
        var credits = new StringBuilder[original.Length + 1];
        Array.Copy(original, 0, credits, 0, insertionIndex);
        credits[insertionIndex] = new StringBuilder("Port made by Gabriel Favero");
        Array.Copy(original, insertionIndex, credits, insertionIndex + 1,
            original.Length - insertionIndex);
        return credits;
    }
}
