using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>
    /// The plane through a palm, in the camera's space at capture (+X right, +Y up, +Z forward, metres). When
    /// Valid is false the other fields say how far the fit got and must not be used to place anything.
    /// </summary>
    public struct PalmPlaneFit
    {
        /// <summary>Enough samples were left and the plane's centre is where LiDAR is trusted on a hand.</summary>
        public bool Valid;
        /// <summary>The plane's centre: the point of the plane under the middle of the samples that were kept.</summary>
        public float3 Point;
        /// <summary>Unit length, pointing to the camera's side of the plane.</summary>
        public float3 Normal;
        /// <summary>Depth samples left after the outlier pass.</summary>
        public int Samples;
        /// <summary>Metres from the camera to the plane's centre along the camera's forward axis: Point.z.</summary>
        public float Distance;
    }

    /// <summary>
    /// Finds the palm in depth (documents/ATLAS_Hands_Design.md section 4.3, step 2). The palm is the largest
    /// flat area of a hand, so the plane through its LiDAR samples is the most trustworthy depth there is on
    /// one: it gives the palm's distance and its tilt, and every palm joint is then put where its ray meets it.
    ///
    /// A plane seen through a pinhole has an inverse depth that is linear in the picture: 1/z = a u + b v + c.
    /// The fit is ordinary least squares of that line over the samples, which puts the error where LiDAR's is
    /// (along the ray) and keeps a far sample from weighing more than a near one.
    /// </summary>
    public static class PalmPlane
    {
        /// <summary>
        /// A sample this far from the middle depth of all the samples is not of the palm at all (the wall
        /// between two fingers, a sleeve) and is dropped before the first fit, metres. A palm at its steepest
        /// spans less than this either side of its centre.
        /// </summary>
        internal const float GrossMetres = 0.12f;

        /// <summary>The samples lie on one line of the picture when the narrow spread is under this share of the wide one, squared.</summary>
        const double CollinearShare = 1e-4;
        /// <summary>The wrist and the four knuckles.</summary>
        const int CornerJoints = 5;
        const int MaxSamples = DetectedHand.MaxPalmSamples + CornerJoints;

        /// <summary>
        /// Least squares over the hand's palm depth samples, and the depth under the wrist and the four knuckles
        /// where LiDAR is sure of it, with one pass of outlier rejection: a sample farther than
        /// <see cref="HandsConstants.PalmOutlierMetres"/> from the first plane is dropped and the plane is
        /// fitted again. Valid with at least <see cref="HandsConstants.PalmMinSamples"/> samples left and its
        /// centre between <see cref="HandsConstants.DepthMinMetres"/> and <see cref="HandsConstants.DepthMaxMetres"/>.
        /// Allocates nothing.
        /// </summary>
        public static unsafe PalmPlaneFit Fit(in HandsDetection detection, in DetectedHand hand)
        {
            PalmPlaneFit fit = default(PalmPlaneFit);
            float fx = detection.Fx, fy = detection.Fy;
            if (!(fx > 0f) || !(fy > 0f)) return fit;

            // One copy: the hand's accessors are not readonly, so each call through `in` would copy it again.
            DetectedHand local = hand;
            int offered = math.clamp(local.PalmCount, 0, DetectedHand.MaxPalmSamples);

            // x, y: the slopes of the sample's ray (camera space); z: its inverse depth.
            float3* samples = stackalloc float3[MaxSamples];
            float* depths = stackalloc float[MaxSamples];
            int count = 0;
            for (int i = 0; i < offered; i++)
            {
                float3 sample = local.GetPalmSample(i);
                if (Take(in detection, sample.xy, sample.z, samples + count)) depths[count++] = sample.z;
            }
            // The wrist and the four knuckles are the palm's corners: the depth under each, where LiDAR is sure
            // of it, is a sample of the same plane with the longest lever of all on its tilt.
            for (int i = 0; i < CornerJoints; i++)
            {
                JointSample joint = local.GetJoint(i == 0 ? 0 : 1 + 4 * i);   // wrist, then each finger's base but the thumb's
                if (!(joint.Confidence >= HandsConstants.JointConfidenceFloor) || joint.DepthConfidence < 1
                    || !(joint.DepthSpread < HandsConstants.JointDepthMaxSpread)) continue;
                if (Take(in detection, joint.Pixel, joint.Depth, samples + count)) depths[count++] = joint.Depth;
            }
            fit.Samples = count;
            if (count < 3) return fit;

            // What is not palm at all goes first, so that it cannot drag the first fit.
            float middle = Median(depths, count);
            int kept = 0;
            for (int i = 0; i < count; i++)
                if (math.abs(1f / samples[i].z - middle) <= GrossMetres) samples[kept++] = samples[i];
            fit.Samples = kept;

            float3 plane;   // plane . p = 1
            float2 centre;  // the middle of the samples in the picture, as ray slopes
            float inverseDepth;
            if (!Solve(samples, kept, out plane, out centre, out inverseDepth)) return fit;

            // One pass of outlier rejection against the first plane.
            float scale = 1f / math.length(plane);
            int inliers = 0;
            for (int i = 0; i < kept; i++)
            {
                float3 s = samples[i];
                float3 point = new float3(s.x, s.y, 1f) / s.z;
                if (math.abs(math.dot(plane, point) - 1f) * scale <= HandsConstants.PalmOutlierMetres) samples[inliers++] = s;
            }
            fit.Samples = inliers;
            if (inliers < kept && !Solve(samples, inliers, out plane, out centre, out inverseDepth)) return fit;

            float length = math.length(plane);
            if (!(length > 0f) || !(inverseDepth > 0f)) return fit;
            fit.Point = new float3(centre.x, centre.y, 1f) / inverseDepth;
            fit.Normal = -plane / length;   // plane . p = 1 puts `plane` on the far side; the camera is on the other
            fit.Distance = fit.Point.z;
            fit.Valid = inliers >= HandsConstants.PalmMinSamples
                        && fit.Distance >= HandsConstants.DepthMinMetres && fit.Distance <= HandsConstants.DepthMaxMetres
                        && math.all(math.isfinite(fit.Normal)) && math.all(math.isfinite(fit.Point));
            return fit;
        }

        /// <summary>One depth sample as the fit wants it: the slopes of its ray and its inverse depth. False for a sample that is no depth at all.</summary>
        static unsafe bool Take(in HandsDetection detection, float2 pixel, float depth, float3* into)
        {
            if (!(depth > 0f) || float.IsInfinity(depth)) return false;   // also false for NaN
            float u = (pixel.x - detection.Cx) / detection.Fx, v = -(pixel.y - detection.Cy) / detection.Fy;
            if (float.IsNaN(u) || float.IsInfinity(u) || float.IsNaN(v) || float.IsInfinity(v)) return false;
            *into = new float3(u, v, 1f / depth);
            return true;
        }

        /// <summary>
        /// The least-squares line of inverse depth over the picture. False when there are too few samples or
        /// they lie on one line of the picture, which fixes no plane.
        /// </summary>
        static unsafe bool Solve(float3* samples, int count, out float3 plane, out float2 centre, out float inverseDepth)
        {
            plane = float3.zero;
            centre = float2.zero;
            inverseDepth = 0f;
            if (count < 3) return false;

            double mu = 0.0, mv = 0.0, mw = 0.0;
            for (int i = 0; i < count; i++)
            {
                mu += samples[i].x;
                mv += samples[i].y;
                mw += samples[i].z;
            }
            mu /= count;
            mv /= count;
            mw /= count;

            double suu = 0.0, suv = 0.0, svv = 0.0, suw = 0.0, svw = 0.0;
            for (int i = 0; i < count; i++)
            {
                double du = samples[i].x - mu, dv = samples[i].y - mv, dw = samples[i].z - mw;
                suu += du * du;
                suv += du * dv;
                svv += dv * dv;
                suw += du * dw;
                svw += dv * dw;
            }
            double determinant = suu * svv - suv * suv, trace = suu + svv;
            if (!(determinant > CollinearShare * trace * trace)) return false;   // also false for NaN

            double a = (suw * svv - svw * suv) / determinant, b = (svw * suu - suw * suv) / determinant;
            // 1/z = a u + b v + c  is  (a, b, c) . (z u, z v, z) = 1.
            plane = new float3((float)a, (float)b, (float)(mw - a * mu - b * mv));
            centre = new float2((float)mu, (float)mv);
            inverseDepth = (float)mw;
            return math.all(math.isfinite(plane)) && mw > 0.0;
        }

        /// <summary>The middle value. Sorts the buffer.</summary>
        static unsafe float Median(float* values, int count)
        {
            for (int i = 1; i < count; i++)
            {
                float value = values[i];
                int j = i - 1;
                while (j >= 0 && values[j] > value)
                {
                    values[j + 1] = values[j];
                    j--;
                }
                values[j + 1] = value;
            }
            return (count & 1) == 1 ? values[count / 2] : 0.5f * (values[count / 2 - 1] + values[count / 2]);
        }
    }
}
