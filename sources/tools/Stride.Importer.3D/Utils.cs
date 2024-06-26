// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Silk.NET.Assimp;
using Stride.Animations;
using Stride.Core.Mathematics;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Stride.Importer.ThreeD
{
    public static class Utils
    {
        public const int AI_MAX_NUMBER_OF_TEXTURECOORDS = 8;
        public const int AI_MAX_NUMBER_OF_COLOR_SETS = 8;

        public static Matrix ToStrideMatrix(this Matrix4x4 matrix)
        {
            // Note the order. Matrices from Assimp has to be transposed
            return new Matrix(
                matrix.M11, matrix.M21, matrix.M31, matrix.M41,
                matrix.M12, matrix.M22, matrix.M32, matrix.M42,
                matrix.M13, matrix.M23, matrix.M33, matrix.M43,
                matrix.M14, matrix.M24, matrix.M34, matrix.M44);
        }

        public static Core.Mathematics.Vector3 ToStrideVector3(this System.Numerics.Vector3 v)
            => Unsafe.As<System.Numerics.Vector3, Core.Mathematics.Vector3>(ref v);

        public static Color ToStrideColor(this System.Numerics.Vector4 v)
            => new Color(v.X, v.Y, v.Z, v.W);

        public static Core.Mathematics.Quaternion ToStrideQuaternion(this AssimpQuaternion q)
            => new Core.Mathematics.Quaternion(q.X, q.Y, q.Z, q.W);

        public static unsafe uint GetNumUVChannels(Silk.NET.Assimp.Mesh* mesh)
        {
            var n = 0;
            while (n < AI_MAX_NUMBER_OF_TEXTURECOORDS && mesh->MTextureCoords[n] != null)
            {
                ++n;
            }

            return (uint)n;
        }

        public static unsafe uint GetNumColorChannels(Silk.NET.Assimp.Mesh* mesh)
        {
            var n = 0;
            while (n < AI_MAX_NUMBER_OF_COLOR_SETS && mesh->MColors[n] != null)
            {
                ++n;
            }

            return (uint)n;
        }

        public static CompressedTimeSpan AiTimeToStrideTimeSpan(double time, double aiTickPerSecond)
        {
            var sdTime = CompressedTimeSpan.TicksPerSecond / aiTickPerSecond * time;
            return new CompressedTimeSpan((int)sdTime);
        }

        public static string CleanNodeName(this string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName)) { return itemName; }
            var itemNameSplitPosition = itemName.IndexOf('#');
            if (itemNameSplitPosition != -1)
            {
                itemName = itemName.Substring(0, itemNameSplitPosition);
            }

            itemNameSplitPosition = itemName.IndexOf("__", StringComparison.Ordinal);
            if (itemNameSplitPosition != -1)
            {
                itemName = itemName.Substring(0, itemNameSplitPosition);
            }

            itemNameSplitPosition = itemName.LastIndexOf(":", StringComparison.Ordinal);
            if (itemNameSplitPosition != -1)
            {
                if (itemName.Length > itemNameSplitPosition + 1)
                {
                    itemName = itemName.Substring(itemNameSplitPosition + 1);
                }
            }

            // remove all bad characters
            itemName = itemName.Replace(':', '_');
            itemName = itemName.Replace(" ", string.Empty);

            return itemName;
        }
    }
}
