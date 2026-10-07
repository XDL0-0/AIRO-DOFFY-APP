using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Doffy.Protocol
{
    /// <summary>Classic wire encodings. Numeric fields never use the headset locale.</summary>
    public static class TeleopWireFormat
    {
        public static string ControllerSide(float px, float py, float pz,
            float qx, float qy, float qz, float qw, float jx, float jy, float trigger,
            int grip, int ax, int by, int stick) => string.Format(CultureInfo.InvariantCulture,
            "{0:F6},{1:F6},{2:F6},{3:F6},{4:F6},{5:F6},{6:F6},{7:F6},{8:F6},{9:F6},{10},{11},{12},{13}",
            px, py, pz, qx, qy, qz, qw, jx, jy, trigger, grip, ax, by, stick);

        public static void AppendFloat(StringBuilder buffer, float value, string precision)
        {
            buffer.Append(',').Append(value.ToString(precision, CultureInfo.InvariantCulture));
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct FloatBits
        {
            [FieldOffset(0)] public float Value;
            [FieldOffset(0)] public uint Bits;
        }

        public static void WriteFloatLittleEndian(byte[] buffer, int offset, float value)
        {
            uint bits = new FloatBits { Value = value }.Bits;
            buffer[offset] = (byte)bits;
            buffer[offset + 1] = (byte)(bits >> 8);
            buffer[offset + 2] = (byte)(bits >> 16);
            buffer[offset + 3] = (byte)(bits >> 24);
        }
    }
}
