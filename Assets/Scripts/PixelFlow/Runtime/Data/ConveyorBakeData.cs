using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixelFlow.Runtime.Data
{
    [Serializable]
    public sealed class ConveyorBakeData
    {
        [SerializeField] private List<Vector3> sampledPoints = new();
        [SerializeField] private List<Vector3> tangents = new();
        [SerializeField] private List<float> cumulativeDistances = new();

        public IReadOnlyList<Vector3> SampledPoints => sampledPoints;
        public IReadOnlyList<Vector3> Tangents => tangents;
        public IReadOnlyList<float> CumulativeDistances => cumulativeDistances;
    }
}
