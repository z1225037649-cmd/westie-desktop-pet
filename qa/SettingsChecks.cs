using System;
using WestiePet;

static class SettingsChecks
{
    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    static int Main()
    {
        try
        {
            Require(PetSettings.ClampSize(double.NaN) == PetSettings.DefaultSize, "NaN size must use the default.");
            Require(PetSettings.ClampSize(double.PositiveInfinity) == PetSettings.DefaultSize, "Infinite size must use the default.");
            Require(PetSettings.ClampSize(-9) == PetSettings.MinimumSize, "Small sizes must be clamped.");
            Require(PetSettings.ClampSize(9) == PetSettings.MaximumSize, "Large sizes must be clamped.");
            Require(PetSettings.ReadNumber("NaN", 12) == 12, "NaN coordinates must use the safe fallback.");
            Require(PetSettings.ReadNumber("Infinity", 12) == 12, "Infinite coordinates must use the safe fallback.");
            Require(PetSettings.ReadNumber("12.5", 0) == 12.5, "Invariant-culture decimal values must load.");
            Require(PetSettings.ReadCount("-4") == 0 && PetSettings.ReadCount("invalid") == 0, "Invalid counts must reset safely.");
            Require(PetSettings.ReadCount("42") == 42, "Stored interaction counts must load.");
            Require(PetSettings.ReadName("   ") == "小白", "Empty names must fall back to the default.");
            Require(PetSettings.ReadName("  小白朋友  ") == "小白朋友", "Names must be trimmed.");
            Require(PetSettings.ReadName("abcdefghijklmn").Length == 12, "Names must respect the display limit.");
            Console.WriteLine("PASS: finite settings, size limits, counters and pet names use safe values.");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL: " + error); return 1; }
    }
}
