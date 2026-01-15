using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MeshUtils
{
    /// <summary>
    /// A blend shape.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Auto)]
    public struct BlendShape
    {
        /// <summary>
        /// The name of the blend shape.
        /// </summary>
        public string ShapeName;
        /// <summary>
        /// The blend shape frames.
        /// </summary>
        public BlendShapeFrame[] Frames;

        /// <summary>
        /// Creates a new blend shape.
        /// </summary>
        /// <param name="shapeName">The name of the blend shape.</param>
        /// <param name="frames">The blend shape frames.</param>
        public BlendShape(string shapeName, BlendShapeFrame[] frames)
        {
            this.ShapeName = shapeName;
            this.Frames = frames;
        }
    }

    /// <summary>
    /// A blend shape frame.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Auto)]
    public struct BlendShapeFrame
    {
        /// <summary>
        /// The weight of the blend shape frame.
        /// </summary>
        public float FrameWeight;
        /// <summary>
        /// The delta vertices of the blend shape frame.
        /// </summary>
        public Vector3[] DeltaVertices;
        /// <summary>
        /// The delta normals of the blend shape frame.
        /// </summary>
        public Vector3[] DeltaNormals;
        /// <summary>
        /// The delta tangents of the blend shape frame.
        /// </summary>
        public Vector3[] DeltaTangents;

        /// <summary>
        /// Creates a new blend shape frame.
        /// </summary>
        /// <param name="frameWeight">The weight of the blend shape frame.</param>
        /// <param name="deltaVertices">The delta vertices of the blend shape frame.</param>
        /// <param name="deltaNormals">The delta normals of the blend shape frame.</param>
        /// <param name="deltaTangents">The delta tangents of the blend shape frame.</param>
        public BlendShapeFrame(float frameWeight, Vector3[] deltaVertices, Vector3[] deltaNormals, Vector3[] deltaTangents)
        {
            this.FrameWeight = frameWeight;
            this.DeltaVertices = deltaVertices;
            this.DeltaNormals = deltaNormals;
            this.DeltaTangents = deltaTangents;
        }
    }
}
