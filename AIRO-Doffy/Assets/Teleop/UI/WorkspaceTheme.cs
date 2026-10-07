using UnityEngine;

namespace Doffy.UI
{
    public static class WorkspaceTheme
    {
        public static readonly Color Background = Hex(0x111923);
        public static readonly Color Surface = Hex(0x1B2633);
        public static readonly Color Raised = Hex(0x263546);
        public static readonly Color Border = Hex(0x354454);
        public static readonly Color Text = Hex(0xF1F4F1);
        public static readonly Color Muted = Hex(0xADBAC7);
        public static readonly Color Accent = Hex(0x9AE3C6);
        public static readonly Color Warning = Hex(0xEDC27A);
        public static readonly Color Danger = Hex(0xED9393);

        private static Color Hex(uint value) => new Color(
            ((value >> 16) & 255) / 255f, ((value >> 8) & 255) / 255f,
            (value & 255) / 255f, 1f);
    }
}
