using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#if POE1
using Color = SharpDX.Color;

using Vector2 = SharpDX.Vector2;
using Vector3 = SharpDX.Vector3;

#else
using Color = System.Drawing.Color;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;


#endif

namespace TradeMonitor.Utilities
{
    public static class ExileCoreVersionHelper
    {

        public static System.Drawing.Color ToStandardColor(this Color c)
        {
            return System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B); ;
        }

        public static Color FromStandardColor(this System.Drawing.Color c)
        {
#if POE1
            return new Color(c.R, c.G, c.B, c.A);
#else
            return c;
#endif
        }

        public static System.Numerics.Vector2 ToStandardVector2(this Vector2 v)
        {
            return new System.Numerics.Vector2(v.X, v.Y);
        }

        public static System.Numerics.Vector3 ToStandardVector3(this Vector3 v)
        {
            return new System.Numerics.Vector3(v.X, v.Y, v.Z);
        }

        // Note, HUD has this but compiler directives seem to make it have trouble finding it?
        public static System.Numerics.Vector2 Xy(this Vector3 v)
        {
            return new System.Numerics.Vector2(v.X, v.Y);
        }
    }
}
