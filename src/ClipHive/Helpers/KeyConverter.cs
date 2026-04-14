namespace ClipHive;

/// <summary>
/// Converts between Windows Virtual Key codes and human-readable display strings.
/// </summary>
public static class KeyConverter
{
    private static readonly IReadOnlyDictionary<uint, string> VkToDisplay =
        new Dictionary<uint, string>
        {
            // Letters
            [0x41] = "A", [0x42] = "B", [0x43] = "C", [0x44] = "D",
            [0x45] = "E", [0x46] = "F", [0x47] = "G", [0x48] = "H",
            [0x49] = "I", [0x4A] = "J", [0x4B] = "K", [0x4C] = "L",
            [0x4D] = "M", [0x4E] = "N", [0x4F] = "O", [0x50] = "P",
            [0x51] = "Q", [0x52] = "R", [0x53] = "S", [0x54] = "T",
            [0x55] = "U", [0x56] = "V", [0x57] = "W", [0x58] = "X",
            [0x59] = "Y", [0x5A] = "Z",

            // Digits
            [0x30] = "0", [0x31] = "1", [0x32] = "2", [0x33] = "3",
            [0x34] = "4", [0x35] = "5", [0x36] = "6", [0x37] = "7",
            [0x38] = "8", [0x39] = "9",

            // Function keys
            [0x70] = "F1",  [0x71] = "F2",  [0x72] = "F3",  [0x73] = "F4",
            [0x74] = "F5",  [0x75] = "F6",  [0x76] = "F7",  [0x77] = "F8",
            [0x78] = "F9",  [0x79] = "F10", [0x7A] = "F11", [0x7B] = "F12",

            // Special keys
            [0x08] = "Backspace",
            [0x09] = "Tab",
            [0x0D] = "Enter",
            [0x1B] = "Esc",
            [0x20] = "Space",
            [0x21] = "Page Up",
            [0x22] = "Page Down",
            [0x23] = "End",
            [0x24] = "Home",
            [0x25] = "Left",
            [0x26] = "Up",
            [0x27] = "Right",
            [0x28] = "Down",
            [0x2D] = "Insert",
            [0x2E] = "Delete",
        };

    private static readonly IReadOnlyDictionary<string, uint> DisplayToVk =
        new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = 0x41, ["B"] = 0x42, ["C"] = 0x43, ["D"] = 0x44,
            ["E"] = 0x45, ["F"] = 0x46, ["G"] = 0x47, ["H"] = 0x48,
            ["I"] = 0x49, ["J"] = 0x4A, ["K"] = 0x4B, ["L"] = 0x4C,
            ["M"] = 0x4D, ["N"] = 0x4E, ["O"] = 0x4F, ["P"] = 0x50,
            ["Q"] = 0x51, ["R"] = 0x52, ["S"] = 0x53, ["T"] = 0x54,
            ["U"] = 0x55, ["V"] = 0x56, ["W"] = 0x57, ["X"] = 0x58,
            ["Y"] = 0x59, ["Z"] = 0x5A,
            ["0"] = 0x30, ["1"] = 0x31, ["2"] = 0x32, ["3"] = 0x33,
            ["4"] = 0x34, ["5"] = 0x35, ["6"] = 0x36, ["7"] = 0x37,
            ["8"] = 0x38, ["9"] = 0x39,
            ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
            ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
            ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
            ["Backspace"] = 0x08,
            ["Tab"] = 0x09,
            ["Enter"] = 0x0D,
            ["Esc"] = 0x1B,
            ["Space"] = 0x20,
            ["Page Up"] = 0x21,
            ["Page Down"] = 0x22,
            ["End"] = 0x23,
            ["Home"] = 0x24,
            ["Left"] = 0x25,
            ["Up"] = 0x26,
            ["Right"] = 0x27,
            ["Down"] = 0x28,
            ["Insert"] = 0x2D,
            ["Delete"] = 0x2E,
        };

    /// <summary>
    /// Converts a Virtual Key code to a human-readable display string.
    /// Returns the hex representation if the key code is unknown.
    /// </summary>
    public static string VirtualKeyToString(uint vk) =>
        VkToDisplay.TryGetValue(vk, out string? display)
            ? display
            : $"0x{vk:X2}";

    /// <summary>
    /// Converts a modifier bitmask to a display string such as "Ctrl+Shift".
    /// </summary>
    public static string ModifiersToString(uint modifiers)
    {
        var parts = new List<string>(4);
        if ((modifiers & Win32.MOD_CTRL)  != 0) parts.Add("Ctrl");
        if ((modifiers & Win32.MOD_ALT)   != 0) parts.Add("Alt");
        if ((modifiers & Win32.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & Win32.MOD_WIN)   != 0) parts.Add("Win");
        return string.Join("+", parts);
    }

    /// <summary>
    /// Returns a full hotkey display string, e.g. "Ctrl+Shift+V".
    /// </summary>
    public static string HotkeyToString(uint modifiers, uint vk) =>
        $"{ModifiersToString(modifiers)}+{VirtualKeyToString(vk)}";

    /// <summary>
    /// Attempts to parse a display string (e.g. "V") to a Virtual Key code.
    /// Returns false if not found.
    /// </summary>
    public static bool TryParseVirtualKey(string display, out uint vk) =>
        DisplayToVk.TryGetValue(display, out vk);
}
