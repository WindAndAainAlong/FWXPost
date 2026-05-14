using System;

namespace PostProcessor.Core.Kinematics;

/// <summary>
/// AC 双摆头解算：
/// A 绕 X，C 绕 Z。
/// 输入刀轴向量 (I,J,K)，输出 A/C 角度。
/// 约束：A 限制在 [-90, 90]，C 限制在 [-360, 360]。
/// </summary>
public static class AcHeadKinematics
{
    public static bool TrySolveAc(double i, double j, double k, out double aDeg, out double cDeg)
    {
        aDeg = 0;
        cDeg = 0;

        var v = new Vector3(i, j, k).Normalized();
        if (v.IsZero)
        {
            return false;
        }

        // A 由刀轴向量与 Z 的夹角决定（范围约为 -180..0）
        var s = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        var aRad = -Math.Atan2(s, v.Z);

        // C 由 XY 投影角度决定（范围 -180..180）
        var cRad = Math.Atan2(-v.X, v.Y);

        aDeg = aRad * 180.0 / Math.PI;
        cDeg = cRad * 180.0 / Math.PI;

        // A 超出 [-90, 90] 时进行折返，并让 C + 180 保持方向一致
        if (aDeg < -90.0)
        {
            aDeg = -180.0 - aDeg; // 例如 -120 -> -60
            cDeg += 180.0;
        }
        else if (aDeg > 90.0)
        {
            aDeg = 180.0 - aDeg;
            cDeg += 180.0;
        }

        cDeg = Normalize360Signed(cDeg);
        return true;
    }

    /// <summary>
    /// 将角度归一到 [-360, 360]。
    /// </summary>
    private static double Normalize360Signed(double deg)
    {
        while (deg > 360.0)
        {
            deg -= 360.0;
        }
        while (deg < -360.0)
        {
            deg += 360.0;
        }
        return deg;
    }

    private readonly struct Vector3
    {
        public Vector3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public bool IsZero => Math.Abs(X) < 1e-12 && Math.Abs(Y) < 1e-12 && Math.Abs(Z) < 1e-12;

        public Vector3 Normalized()
        {
            var len = Math.Sqrt(X * X + Y * Y + Z * Z);
            if (len < 1e-12)
            {
                return this;
            }
            return new Vector3(X / len, Y / len, Z / len);
        }
    }
}
