using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Doffy.Protocol
{
    /// <summary>Classic 8012 contract. Position is meters; rotation is w,x,y,z.</summary>
    public sealed class TcpStatePacket
    {
        [Serializable]
        public class Pose
        {
            public float[] position, rotation, force, torque;
        }
        public Pose leftTCP, rightTCP;

        public static bool TryParse(byte[] data, out TcpStatePacket packet)
        {
            packet = null;
            if (data == null || data.Length == 0 || data.Length > 65507) return false;
            try
            {
                JObject json;
                using (var reader = new JsonTextReader(new StringReader(Encoding.UTF8.GetString(data))) { MaxDepth = 16 })
                { json = JObject.Load(reader); if (reader.Read()) return false; }
                Pose left, right;
                if (!ReadPose(json["leftTCP"], out left) || !ReadPose(json["rightTCP"], out right) ||
                    (left == null && right == null)) return false;
                packet = new TcpStatePacket { leftTCP = left, rightTCP = right };
                return true;
            }
            catch (JsonException) { return false; }
            catch (FormatException) { return false; }
            catch (OverflowException) { return false; }
        }

        private static bool ReadPose(JToken token, out Pose pose)
        {
            pose = null;
            if (token == null || token.Type == JTokenType.Null) return true;
            if (!(token is JObject)) return false;
            float[] p, q, f, t;
            if (!ReadVector(token["position"], 3, out p) || !ReadVector(token["rotation"], 4, out q) ||
                !ReadVector(token["force"], 3, out f) || !ReadVector(token["torque"], 3, out t)) return false;
            if (p == null && q == null && f == null && t == null) return false;
            if (q != null)
            {
                double norm = 0;
                foreach (float value in q) norm += (double)value * value;
                if (norm < 1e-12) return false;
                norm = Math.Sqrt(norm);
                for (int i = 0; i < q.Length; i++) q[i] = (float)(q[i] / norm);
            }
            pose = new Pose { position = p, rotation = q, force = f, torque = t };
            return true;
        }

        private static bool ReadVector(JToken token, int length, out float[] values)
        {
            values = null;
            if (token == null || token.Type == JTokenType.Null) return true;
            var array = token as JArray;
            if (array == null || array.Count != length) return false;
            values = new float[length];
            for (int i = 0; i < length; i++)
            {
                if (array[i].Type != JTokenType.Float && array[i].Type != JTokenType.Integer) return false;
                values[i] = array[i].Value<float>();
                if (float.IsNaN(values[i]) || float.IsInfinity(values[i])) return false;
            }
            return true;
        }
    }
}
