using Rhino;
using Rhino.Geometry;
using Rhino.Display;

namespace RhinoGltf
{
    /// <summary>
    /// Functions for helping with adding RhinoObjects to the RootModel.
    /// </summary>
    public static class GlTFUtils
    {
        public static float[] GetMatrix(Transform t, RhinoDoc doc)
        {
            var scale = RhinoMath.MetersPerUnit(doc.ModelUnitSystem);

            Transform r = default;
            r.M00 = t.M00;
            r.M01 = t.M02;
            r.M02 = -t.M01;
            r.M03 = t.M03 * scale;

            r.M10 = t.M20;
            r.M11 = t.M22;
            r.M12 = -t.M21;
            r.M13 = t.M23 * scale;

            r.M20 = -t.M10;
            r.M21 = -t.M12;
            r.M22 = t.M11;
            r.M23 = -t.M13 * scale;

            r.M30 = t.M30;
            r.M31 = t.M32;
            r.M32 = -t.M31;
            r.M33 = t.M33;

            return r.ToFloatArray(false);
        }

        public static int AddAndReturnIndex<T>(this List<T> list, T item)
        {
            list.Add(item);
            return list.Count - 1;
        }

        public static bool IsFileGltfBinary(string filename)
        {
            string extension = Path.GetExtension(filename);

            return extension.ToLower() == ".glb";
        }

        public static float[] ToFloatArray(this Color4f color)
        {
            return new float[]
            {
                color.R,
                color.G,
                color.B,
                color.A,
            };
        }

        public static float[] ToFloatArray(this Point3d point)
        {
            return new float[]
            {
                (float)point.X,
                (float)point.Y,
                (float)point.Z,
            };
        }

        public static float[] ToFloatArray(this Vector3f vector)
        {
            return new float[]
            {
                vector.X,
                vector.Y,
                vector.Z,
            };
        }

        public static float[] ToFloatArray(this Point2f point)
        {
            return new float[]
            {
                point.X,
                point.Y,
            };
        }
    }
}
