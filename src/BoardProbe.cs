internal static class Probe {
    public static void Main(string[] args) {
        if (args.Length != 1 || !System.Text.RegularExpressions.Regex.IsMatch(args[0], "^[0-9A-Fa-f]{6}$")) return;
        System.Console.WriteLine(BoardRgb.Apply(args[0]));
    }
}
